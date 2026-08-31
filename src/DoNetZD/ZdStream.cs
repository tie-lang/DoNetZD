using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DoNetZD;

/// <summary>
/// 流式编解码 + 异步文件 IO。
/// <para>_encode/Decode 直接读写 <see cref="Stream"/>，不把整个结构物化为单个 byte[]，
/// 适合大体积嵌套数据（如照片元数据树）流过管线。</para>
/// <para>SaveAsync/LoadAsync 在 netstandard2.0 上以 FileStream + ReadAsync/WriteAsync 实现。</para>
/// </summary>
public static class ZdStream
{
    // ==================== 编码（ZdValue → Stream）====================

    /// <summary>把 zd 值直接编码写入流（标量/容器头复用 Zd.* 的小字节表，容器元素递归直写）。</summary>
    public static void Encode(Stream stream, ZdValue v)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (v is null) throw new ArgumentNullException(nameof(v));
        switch (v)
        {
            case ZdValue.Integer i: WriteAll(stream, Zd.EncodeI64(i.Value)); break;
            case ZdValue.Float f: WriteAll(stream, Zd.EncodeF64(f.Value)); break;
            case ZdValue.Bool b: stream.WriteByte(b.Value ? Zd.TagTrue : Zd.TagFalse); break;
            case ZdValue.Char c:
                stream.WriteByte(Zd.TagChar);
                WriteAll(stream, Zd.WriteU32Be((uint)c.Codepoint));
                break;
            case ZdValue.Trit t: stream.WriteByte(Zd.TagTrit); stream.WriteByte((byte)(t.Value & 0xFF)); break;
            case ZdValue.String s: WriteAll(stream, Zd.EncodeString(s.Value)); break;
            case ZdValue.Null: stream.WriteByte(Zd.TagNull); break;
            case ZdValue.Bytes by: WriteAll(stream, Zd.EncodeBytes(by.Content)); break;
            case ZdValue.Ext ex: WriteAll(stream, Zd.EncodeExt(ex.TypeTag, ex.Payload)); break;
            case ZdValue.Array a:
                WriteAll(stream, Zd.EncodeArrayHeader(a.Items.Count));
                for (int i = 0; i < a.Items.Count; i++)
                    Encode(stream, a.Items[i]);
                break;
            case ZdValue.Map m:
                WriteAll(stream, Zd.EncodeMapHeader(m.Entries.Count));
                foreach (var kv in m.Entries)
                {
                    WriteAll(stream, Zd.EncodeString(kv.Key));
                    Encode(stream, kv.Value);
                }
                break;
            default: throw new ArgumentException($"未知 zd 值类型 {v.GetType().Name}");
        }
    }

    private static void WriteAll(Stream s, byte[] bytes) => s.Write(bytes, 0, bytes.Length);

    // ==================== 解码（Stream → ZdValue）====================

    /// <summary>从流解码一个 zd 值（从当前位置起；允许尾随字节）。</summary>
    public static ZdValue Decode(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        return new StreamReader(stream).DecodeValue();
    }

    private sealed class StreamReader
    {
        private readonly Stream _s;
        private readonly byte[] _buf = new byte[8192];
        private int _pos, _len;

        public StreamReader(Stream s) { _s = s; }

        private bool Fill()
        {
            if (_pos >= _len)
            {
                _len = _s.Read(_buf, 0, _buf.Length);
                _pos = 0;
            }
            return _pos < _len;
        }

        private byte ReadByte(int start)
        {
            if (!Fill())
                throw new ZdFormatException("流提前结束", start);
            return _buf[_pos++];
        }

        private byte[] ReadBytes(int n, int start)
        {
            var r = new byte[n];
            int got = 0;
            while (got < n)
            {
                if (_pos >= _len && !Fill())
                    throw new ZdFormatException("流提前结束", start);
                int take = _len - _pos;
                if (take > n - got) take = n - got;
                Buffer.BlockCopy(_buf, _pos, r, got, take);
                _pos += take;
                got += take;
            }
            return r;
        }

        private ushort ReadU16(int start) => (ushort)((ReadByte(start) << 8) | ReadByte(start));
        private uint ReadU32(int start) => (uint)((ReadByte(start) << 24) | (ReadByte(start) << 16) | (ReadByte(start) << 8) | ReadByte(start));

        private long ReadArrayLen(int start)
        {
            byte tag = ReadByte(start);
            if (tag >= 0x90 && tag <= 0x9F) return tag - 0x90;
            if (tag == Zd.TagArray16) return ReadU16(start);
            if (tag == Zd.TagArray32) return ReadU32(start);
            throw new ZdFormatException($"期望数组头，实际 0x{tag:X2}", start);
        }

        private long ReadVarint(int start)
        {
            ulong v = 0;
            int shift = 0;
            while (true)
            {
                byte b = ReadByte(start);
                v |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
                if (shift > 63)
                    throw new ZdFormatException("varint 过长", start);
            }
            return (long)v;
        }
        private ulong ReadU64(int start)
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++)
                v = (v << 8) | ReadByte(start);
            return v;
        }

        public ZdValue DecodeValue()
        {
            int start = _pos;   // 仅作错误定位近似值（流无绝对偏移）
            // Fill 一次确保至少能读到 tag
            Fill();
            if (_pos >= _len)
                throw new ZdFormatException("流为空", start);
            byte tag = ReadByte(start);

            if (tag <= 0x7F) return new ZdValue.Integer(tag);
            if (tag >= 0xE0) return new ZdValue.Integer((sbyte)tag);
            if (tag >= 0x80 && tag <= 0x8F) return DecodeMap(tag - 0x80, start);
            if (tag >= 0x90 && tag <= 0x9F) return DecodeArray(tag - 0x90, start);
            if (tag >= 0xA0 && tag <= 0xBF) return DecodeString(tag - 0xA0, start);

            switch (tag)
            {
                case Zd.TagNull: return ZdValue.Null.Instance;
                case Zd.TagFalse: return new ZdValue.Bool(false);
                case Zd.TagTrue: return new ZdValue.Bool(true);
                case Zd.TagChar: return new ZdValue.Char(unchecked((int)ReadU32(start)));
                case Zd.TagTrit: return new ZdValue.Trit((sbyte)ReadByte(start));
                case Zd.TagBytes:
                    {
                        long blen = ReadArrayLen(start);
                        if (blen < 0 || blen > int.MaxValue)
                            throw new ZdFormatException("bytes 长度越界", start);
                        return new ZdValue.Bytes(ReadBytes((int)blen, start));
                    }
                case Zd.TagExt:
                    {
                        long tt = ReadVarint(start);
                        long el = ReadVarint(start);
                        if (el < 0 || el > int.MaxValue)
                            throw new ZdFormatException("ext 载荷长度越界", start);
                        return new ZdValue.Ext(tt, ReadBytes((int)el, start));
                    }
                case Zd.TagF32: return new ZdValue.Float(BitSingle(ReadU32(start)));
                case Zd.TagF64: return new ZdValue.Float(BitDouble(ReadU64(start)));
                case Zd.TagU8: return new ZdValue.Integer(ReadByte(start));
                case Zd.TagU16: return new ZdValue.Integer(ReadU16(start));
                case Zd.TagU32: return new ZdValue.Integer(ReadU32(start));
                case Zd.TagU64: return new ZdValue.Integer(unchecked((long)ReadU64(start)));
                case Zd.TagI8: return new ZdValue.Integer((sbyte)ReadByte(start));
                case Zd.TagI16: return new ZdValue.Integer((short)ReadU16(start));
                case Zd.TagI32: return new ZdValue.Integer((int)ReadU32(start));
                case Zd.TagI64: return new ZdValue.Integer((long)ReadU64(start));
                case Zd.TagStr8: return DecodeString(ReadByte(start), start);
                case Zd.TagStr16: return DecodeString(ReadU16(start), start);
                case Zd.TagStr32: return DecodeString((int)ReadU32(start), start);
                case Zd.TagArray16: return DecodeArray(ReadU16(start), start);
                case Zd.TagArray32: return DecodeArray((int)ReadU32(start), start);
                case Zd.TagMap16: return DecodeMap(ReadU16(start), start);
                case Zd.TagMap32: return DecodeMap((int)ReadU32(start), start);
                default: throw new ZdFormatException($"未知 zd 标签 0x{tag:X2}", start);
            }
        }

        private ZdValue DecodeString(long len, int start)
        {
            if (len < 0 || len > int.MaxValue)
                throw new ZdFormatException("字符串长度越界", start);
            byte[] bytes = ReadBytes((int)len, start);
            return new ZdValue.String(Encoding.UTF8.GetString(bytes));
        }

        private ZdValue DecodeArray(int count, int start)
        {
            var items = new ZdValue[count];
            for (int i = 0; i < count; i++)
                items[i] = DecodeValue();
            return new ZdValue.Array(items);
        }

        private ZdValue DecodeMap(int count, int start)
        {
            var entries = new Dictionary<string, ZdValue>(count);
            for (int i = 0; i < count; i++)
            {
                ZdValue key = DecodeValue();
                if (key is not ZdValue.String ks)
                    throw new ZdFormatException("map 键必须为字符串", start);
                entries[ks.Value] = DecodeValue();
            }
            return new ZdValue.Map(entries);
        }

        private static float BitSingle(uint bits)
        {
            byte[] b = { (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits };
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToSingle(b, 0);
        }

        private static double BitDouble(ulong bits)
        {
            var b = new byte[8];
            for (int i = 0; i < 8; i++)
                b[i] = (byte)(bits >> (56 - i * 8));
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToDouble(b, 0);
        }
    }

    // ==================== 异步文件 IO ====================

    /// <summary>异步把 zd 字节（含魔数头）写入文件。失败返回 false。</summary>
    public static async Task<bool> SaveAsync(string path, byte[] bytes)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            byte[] header = Zd.V2Header(0);
            await fs.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
            byte[] body = bytes ?? Array.Empty<byte>();
            await fs.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>异步读取 zd 文件：校验魔数头、去头返回正文；非 zd 文件返回空数组。</summary>
    public static async Task<byte[]> LoadAsync(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            using var ms = new MemoryStream();
            byte[] buf = new byte[8192];
            int n;
            while ((n = await fs.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                ms.Write(buf, 0, n);
            byte[] data = ms.ToArray();
            if (Zd.DetectVersion(data) == ZdVersion.Unknown)
                return Array.Empty<byte>();
            return Zd.ExtractBody(data);
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }
}
