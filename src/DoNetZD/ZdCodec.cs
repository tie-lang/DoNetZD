using System.Text;

namespace DoNetZD;

/// <summary>
/// tie:zd 类型化解码/编码：递归遍历 ZdValue，与原语层（Zd.*）互通。
/// Encode 把 ZdValue 写成 zd 字节；Decode 按标签递归还原 ZdValue。
/// 若需字节级控制（f32 精确、逐字段组装），直接用原语 Zd.*。
/// </summary>
public static class ZdCodec
{
    // ==================== 编码 ====================

    /// <summary>把一个 zd 值编码为字节（对应 zd.encode_* 的组装）。</summary>
    public static byte[] Encode(ZdValue v)
    {
        if (v is null)
            throw new ArgumentNullException(nameof(v));
        var b = new ZdBuilder(Estimate(v));
        EncodeInto(b, v);
        return b.ToArray();
    }

    private static void EncodeInto(ZdBuilder b, ZdValue v)
    {
        switch (v)
        {
            case ZdValue.Integer i: b.AppendBytes(Zd.EncodeI64(i.Value)); break;
            case ZdValue.Float f: b.AppendBytes(Zd.EncodeF64(f.Value)); break;   // 类型层浮点统一 f64
            case ZdValue.Bool bo: b.AppendByte(bo.Value ? Zd.TagTrue : Zd.TagFalse); break;
            case ZdValue.Char c:
                b.AppendByte(Zd.TagChar);
                b.AppendBytes(Zd.WriteU32Be((uint)c.Codepoint));
                break;
            case ZdValue.Trit t:
                b.AppendByte(Zd.TagTrit);
                b.AppendByte((byte)(t.Value & 0xFF));
                break;
            case ZdValue.String s: b.AppendBytes(Zd.EncodeString(s.Value)); break;
            case ZdValue.Array a:
                b.AppendBytes(Zd.EncodeArrayHeader(a.Items.Count));
                for (int i = 0; i < a.Items.Count; i++)
                    EncodeInto(b, a.Items[i]);
                break;
            case ZdValue.Map m:
                b.AppendBytes(Zd.EncodeMapHeader(m.Entries.Count));
                foreach (var kv in m.Entries)
                {
                    b.AppendBytes(Zd.EncodeString(kv.Key));
                    EncodeInto(b, kv.Value);
                }
                break;
            case ZdValue.Null _: b.AppendByte(Zd.TagNull); break;
            case ZdValue.Bytes by:
                b.AppendByte(Zd.TagBytes);
                b.AppendBytes(Zd.EncodeArrayHeader(by.Content.Length));
                b.AppendBytes(by.Content);
                break;
            case ZdValue.Ext ex:
                b.AppendByte(Zd.TagExt);
                b.AppendBytes(Zd.WriteVarint(ex.TypeTag));
                b.AppendBytes(Zd.WriteVarint(ex.Payload.Length));
                b.AppendBytes(ex.Payload);
                break;
            default:
                throw new ArgumentException($"未知 zd 值类型 {v.GetType().Name}");
        }
    }

    /// <summary>粗略估算编码后字节数，用于缓冲预分配（低估无害，builder 会扩容）。</summary>
    private static int Estimate(ZdValue v) => v switch
    {
        ZdValue.Array a => 16 + a.Items.Count * 8,
        ZdValue.Map m => 16 + m.Entries.Count * 24,
        _ => 64,
    };

    // ==================== 解码 ====================

    /// <summary>把一段 zd 字节解码为一个 zd 值（从位置 0 起解析；允许尾随字节）。</summary>
    public static ZdValue Decode(byte[] data)
        => Decode(data, ZdVersion.V2);

    /// <summary>按目标版本解码（v1 需标志 0xc6 为 tuple 的旧语义兼容；v2 为 bytes）。</summary>
    public static ZdValue Decode(byte[] data, ZdVersion version)
    {
        if (data is null || data.Length == 0)
            throw new ZdFormatException("zd 数据为空", 0);
        int pos = 0;
        return DecodeValue(data, ref pos, version);
    }

    private static ZdValue DecodeValue(byte[] t, ref int pos, ZdVersion ver)
    {
        int start = pos;
        if (!TryByte(t, ref pos, out byte tag))
            throw new ZdFormatException("字节不足：缺少类型标签", start);

        // 整数字段优先（fixint / 定宽）……
        if (tag <= 0x7F)
            return new ZdValue.Integer(tag);                    // fixint 正 0..127
        if (tag >= 0xE0)
            return new ZdValue.Integer((sbyte)tag);             // fixint 负 -32..-1
        if (tag >= 0x80 && tag <= 0x8F)
            return DecodeMap(t, ref pos, tag - 0x80, ver);           // fixmap
        if (tag >= 0x90 && tag <= 0x9F)
            return DecodeArray(t, ref pos, tag - 0x90, ver);         // fixarray
        if (tag >= 0xA0 && tag <= 0xBF)
            return DecodeString(t, ref pos, tag - 0xA0);        // fixstr

        switch (tag)
        {
            case Zd.TagNull: return ZdValue.Null.Instance;
            case Zd.TagFalse: return new ZdValue.Bool(false);
            case Zd.TagTrue: return new ZdValue.Bool(true);
            case Zd.TagChar:
                if (!TryU32(t, ref pos, out uint cp))
                    throw new ZdFormatException("char 数据不完整", start);
                return new ZdValue.Char(unchecked((int)cp));
            case Zd.TagTrit:
                if (!TryByte(t, ref pos, out byte tr))
                    throw new ZdFormatException("trit 数据不完整", start);
                return new ZdValue.Trit((sbyte)tr);
            case Zd.TagF32:
                if (!TryU32(t, ref pos, out uint fz))
                    throw new ZdFormatException("f32 数据不完整", start);
                return new ZdValue.Float(BitSingleFromBe(fz));
            case Zd.TagF64:
                if (!TryU64(t, ref pos, out ulong fd))
                    throw new ZdFormatException("f64 数据不完整", start);
                return new ZdValue.Float(BitDoubleFromBe(fd));
            case Zd.TagU8:
                if (!TryByte(t, ref pos, out byte u8))
                    throw new ZdFormatException("u8 数据不完整", start);
                return new ZdValue.Integer(u8);
            case Zd.TagU16:
                return new ZdValue.Integer(ReadU16(t, ref pos, start));
            case Zd.TagU32:
                return new ZdValue.Integer(ReadU32(t, ref pos, start));
            case Zd.TagU64:
                return new ZdValue.Integer(unchecked((long)ReadU64(t, ref pos, start)));
            case Zd.TagI8:
                if (!TryByte(t, ref pos, out byte i8b))
                    throw new ZdFormatException("i8 数据不完整", start);
                return new ZdValue.Integer((sbyte)i8b);
            case Zd.TagI16:
                return new ZdValue.Integer((short)ReadU16(t, ref pos, start));
            case Zd.TagI32:
                return new ZdValue.Integer((int)ReadU32(t, ref pos, start));
            case Zd.TagI64:
                return new ZdValue.Integer((long)ReadU64(t, ref pos, start));
            case Zd.TagStr8:
                if (!TryByte(t, ref pos, out byte sl8))
                    throw new ZdFormatException("str8 长度缺失", start);
                return DecodeString(t, ref pos, sl8);
            case Zd.TagStr16:
                return DecodeString(t, ref pos, ReadU16(t, ref pos, start));
            case Zd.TagStr32:
                return DecodeString(t, ref pos, ReadU32(t, ref pos, start));
            case Zd.TagArray16:
            case Zd.TagArray32:
                int ac = tag == Zd.TagArray16 ? (int)ReadU16(t, ref pos, start) : unchecked((int)ReadU32(t, ref pos, start));
                return DecodeArray(t, ref pos, ac, ver);
            case Zd.TagMap16:
            case Zd.TagMap32:
                int mc = tag == Zd.TagMap16 ? (int)ReadU16(t, ref pos, start) : unchecked((int)ReadU32(t, ref pos, start));
                return DecodeMap(t, ref pos, mc, ver);
            case Zd.TagBytes:                                   // 0xC6 v2 bytes（v1 旧义 tuple，核心模型不支持）
                if (ver == ZdVersion.V1)
                    throw new ZdFormatException("v1 中 0xc6 为 tuple，核心模型不支持；请改用解码字节层", start);
                {
                    long blen = Zd.ReadArrayLength(t, ref pos);
                    if (blen < 0 || blen > int.MaxValue || pos + blen > t.Length)
                        throw new ZdFormatException("bytes 长度越界", start);
                    byte[] raw = new byte[blen];
                    Array.Copy(t, pos, raw, 0, (int)blen);
                    pos += (int)blen;
                    return new ZdValue.Bytes(raw);
                }
            case Zd.TagExt:                                     // 0xD7 v2 ext
                {
                    long typeTag = Zd.ReadVarint(t, ref pos);
                    long elen = Zd.ReadVarint(t, ref pos);
                    if (elen < 0 || elen > int.MaxValue || pos + elen > t.Length)
                        throw new ZdFormatException("ext 载荷长度越界", start);
                    var payload = new byte[elen];
                    Array.Copy(t, pos, payload, 0, (int)elen);
                    pos += (int)elen;
                    return new ZdValue.Ext(typeTag, payload);
                }
            default:
                throw new ZdFormatException($"未知 zd 标签 0x{tag:X2}", start);
        }
    }

    private static ZdValue DecodeString(byte[] t, ref int pos, long byteLen)
    {
        if (byteLen < 0 || byteLen > int.MaxValue || pos + byteLen > t.Length)
            throw new ZdFormatException("字符串长度越界", pos);
        var bytes = new byte[byteLen];
        Array.Copy(t, pos, bytes, 0, byteLen);
        pos += (int)byteLen;
        return new ZdValue.String(Encoding.UTF8.GetString(bytes));
    }

    private static ZdValue DecodeArray(byte[] t, ref int pos, int count, ZdVersion ver)
    {
        var items = new ZdValue[count];
        for (int i = 0; i < count; i++)
            items[i] = DecodeValue(t, ref pos, ver);
        return new ZdValue.Array(items);
    }

    private static ZdValue DecodeMap(byte[] t, ref int pos, int count, ZdVersion ver)
    {
        var entries = new Dictionary<string, ZdValue>(count);
        for (int i = 0; i < count; i++)
        {
            ZdValue key = DecodeValue(t, ref pos, ver);
            if (key is not ZdValue.String ks)
                throw new ZdFormatException("map 键必须为字符串", pos);
            ZdValue val = DecodeValue(t, ref pos, ver);
            entries[ks.Value] = val;
        }
        return new ZdValue.Map(entries);
    }

    // ==================== 读取辅助（大端）====================

    private static bool TryByte(byte[] t, ref int pos, out byte value)
    {
        if (pos >= t.Length)
        {
            value = 0;
            return false;
        }
        value = t[pos++];
        return true;
    }

    private static bool TryU32(byte[] t, ref int pos, out uint value)
    {
        if (pos + 4 > t.Length)
        {
            value = 0;
            return false;
        }
        value = (uint)((t[pos] << 24) | (t[pos + 1] << 16) | (t[pos + 2] << 8) | t[pos + 3]);
        pos += 4;
        return true;
    }

    private static bool TryU64(byte[] t, ref int pos, out ulong value)
    {
        if (pos + 8 > t.Length)
        {
            value = 0;
            return false;
        }
        value = 0;
        for (int i = 0; i < 8; i++)
            value = (value << 8) | t[pos + i];
        pos += 8;
        return true;
    }

    private static long ReadU16(byte[] t, ref int pos, int start)
    {
        if (pos + 2 > t.Length)
            throw new ZdFormatException("定宽整数越界", start);
        long v = (t[pos] << 8) | t[pos + 1];
        pos += 2;
        return v;
    }

    private static uint ReadU32(byte[] t, ref int pos, int start)
    {
        if (pos + 4 > t.Length)
            throw new ZdFormatException("定宽整数越界", start);
        uint v = (uint)((t[pos] << 24) | (t[pos + 1] << 16) | (t[pos + 2] << 8) | t[pos + 3]);
        pos += 4;
        return v;
    }

    private static ulong ReadU64(byte[] t, ref int pos, int start)
    {
        if (!TryU64(t, ref pos, out ulong v))
            throw new ZdFormatException("定宽整数越界", start);
        return v;
    }

    // 大端字节 → 浮点（平台无关）
    private static float BitSingleFromBe(uint bits)
    {
        byte[] b = { (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits };
        if (BitConverter.IsLittleEndian)
            Array.Reverse(b);
        return BitConverter.ToSingle(b, 0);
    }

    private static double BitDoubleFromBe(ulong bits)
    {
        var b = new byte[8];
        for (int i = 0; i < 8; i++)
            b[i] = (byte)(bits >> (56 - i * 8));
        if (BitConverter.IsLittleEndian)
            Array.Reverse(b);
        return BitConverter.ToDouble(b, 0);
    }
}