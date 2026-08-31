using System.Text;

namespace DoNetZD;

/// <summary>
/// zd 字节可视化转储：把字节（可带 "TIEDBZD" 魔数头）渲染为
/// “偏移 [十六进制] 类型 注解” 的缩进文本树。用于调试与跨语言字节布局对账。
/// 不修改字节；遇到尾随字节会标注未解析。
/// </summary>
public static class ZdDump
{
    private const string Base48Charset = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKL";

    /// <summary>base48 版本位字节 → 字符（0..47 映射到 '0'..'L'）。</summary>
    private static char Base48Digits(byte b) => b < 48 ? Base48Charset[b] : '?';

    /// <summary>把 zd 字节（可带魔数头）转储为可读文本。</summary>
    public static string Dump(byte[] data)
    {
        if (data is null || data.Length == 0)
            return "(empty)";

        var sb = new StringBuilder();
        int pos = 0;
        ZdVersion ver = Zd.DetectVersion(data);
        if (ver != ZdVersion.Unknown)
        {
            sb.Append($"@0  [TIEDBZD ");
            if (ver == ZdVersion.V2)
                sb.Append($"v2 v.{Base48Digits(data[7])}{Base48Digits(data[8])} flags 0x{Zd.GetFlags(data):X2}]\n");
            else
                sb.Append($"v1 v.{data[7]}]\n");
            pos = Zd.HeaderLength(ver);
        }
        DumpValue(sb, data, ref pos, 0);
        if (pos < data.Length)
            sb.Append($"@{pos}  ... 尾随 {data.Length - pos} 字节未解析\n");
        return sb.ToString().TrimEnd();
    }

    private static void DumpValue(StringBuilder sb, byte[] t, ref int pos, int depth)
    {
        string pad = new(' ', depth * 2);
        int start = pos;
        if (pos >= t.Length)
        {
            sb.Append($"{pad}@{start} <EOF>\n");
            return;
        }
        byte tag = t[pos++];

        if (tag <= 0x7F)
        {
            sb.Append($"{pad}@{start} [0x{tag:X2}] fixint+ {tag}\n");
            return;
        }
        if (tag >= 0xE0)
        {
            sb.Append($"{pad}@{start} [0x{tag:X2}] fixint- {(sbyte)tag}\n");
            return;
        }
        if (tag >= 0x80 && tag <= 0x8F)
        {
            DumpMap(sb, t, ref pos, tag - 0x80, start, depth, pad);
            return;
        }
        if (tag >= 0x90 && tag <= 0x9F)
        {
            DumpArray(sb, t, ref pos, tag - 0x90, start, depth, pad);
            return;
        }
        if (tag >= 0xA0 && tag <= 0xBF)
        {
            DumpString(sb, t, ref pos, tag - 0xA0, start, pad);
            return;
        }

        switch (tag)
        {
            case Zd.TagFalse: sb.Append($"{pad}@{start} [0xC2] bool false\n"); return;
            case Zd.TagTrue: sb.Append($"{pad}@{start} [0xC3] bool true\n"); return;
            case Zd.TagChar:
                {
                    uint cp = ReadU32(t, ref pos, start);
                    sb.Append($"{pad}@{start} [0xC4] char U+{cp:X4} '{SafeChar((int)cp)}'\n");
                    return;
                }
            case Zd.TagTrit:
                {
                    byte tr = ReadByte(t, ref pos, start);
                    sb.Append($"{pad}@{start} [0xC5] trit {(sbyte)tr}\n");
                    return;
                }
            case Zd.TagF32:
                {
                    uint bits = ReadU32(t, ref pos, start);
                    sb.Append($"{pad}@{start} [0xCA] f32 0x{bits:X8}\n");
                    return;
                }
            case Zd.TagF64:
                {
                    ulong bits = ReadU64(t, ref pos, start);
                    sb.Append($"{pad}@{start} [0xCB] f64 0x{bits:X16}\n");
                    return;
                }
            case Zd.TagU8:
                {
                    byte v = ReadByte(t, ref pos, start);
                    sb.Append($"{pad}@{start} [0xCC] u8 {v}\n");
                    return;
                }
            case Zd.TagU16:
                {
                    ushort v = ReadU16(t, ref pos, start);
                    sb.Append($"{pad}@{start} [0xCD] u16 {v}\n");
                    return;
                }
            case Zd.TagU32:
                {
                    uint v = ReadU32(t, ref pos, start);
                    sb.Append($"{pad}@{start} [0xCE] u32 {v}\n");
                    return;
                }
            case Zd.TagU64:
                {
                    ulong v = ReadU64(t, ref pos, start);
                    sb.Append($"{pad}@{start} [0xCF] u64 {v}\n");
                    return;
                }
            case Zd.TagI8:
                {
                    sbyte v = (sbyte)ReadByte(t, ref pos, start);
                    sb.Append($"{pad}@{start} [0xD0] i8 {v}\n");
                    return;
                }
            case Zd.TagI16:
                {
                    short v = (short)ReadU16(t, ref pos, start);
                    sb.Append($"{pad}@{start} [0xD1] i16 {v}\n");
                    return;
                }
            case Zd.TagI32:
                {
                    int v = (int)ReadU32(t, ref pos, start);
                    sb.Append($"{pad}@{start} [0xD2] i32 {v}\n");
                    return;
                }
            case Zd.TagI64:
                {
                    long v = (long)ReadU64(t, ref pos, start);
                    sb.Append($"{pad}@{start} [0xD3] i64 {v}\n");
                    return;
                }
            case Zd.TagStr8:
                {
                    int n = ReadByte(t, ref pos, start);
                    DumpString(sb, t, ref pos, n, start, pad);
                    return;
                }
            case Zd.TagStr16:
                {
                    int n = ReadU16(t, ref pos, start);
                    DumpString(sb, t, ref pos, n, start, pad);
                    return;
                }
            case Zd.TagStr32:
                {
                    int n = (int)ReadU32(t, ref pos, start);
                    DumpString(sb, t, ref pos, n, start, pad);
                    return;
                }
            case Zd.TagArray16:
                {
                    int n = ReadU16(t, ref pos, start);
                    DumpArray(sb, t, ref pos, n, start, depth, pad);
                    return;
                }
            case Zd.TagArray32:
                {
                    int n = (int)ReadU32(t, ref pos, start);
                    DumpArray(sb, t, ref pos, n, start, depth, pad);
                    return;
                }
            case Zd.TagMap16:
                {
                    int n = ReadU16(t, ref pos, start);
                    DumpMap(sb, t, ref pos, n, start, depth, pad);
                    return;
                }
            case Zd.TagMap32:
                {
                    int n = (int)ReadU32(t, ref pos, start);
                    DumpMap(sb, t, ref pos, n, start, depth, pad);
                    return;
                }
            case Zd.TagNull:
                sb.Append($"{pad}@{start} [0xC0] null\n");
                return;
            case Zd.TagBytes:
                {
                    long blen = Zd.ReadArrayLength(t, ref pos);
                    if (blen < 0 || blen > int.MaxValue || pos + blen > t.Length)
                    {
                        sb.Append($"{pad}@{start} [0xC6] bytes 长度越界\n");
                        return;
                    }
                    sb.Append($"{pad}@{start} [0xC6] bytes {blen}B {HexRange(t, pos, (int)blen)}\n");
                    pos += (int)blen;
                    return;
                }
            case Zd.TagSchema:
                {
                    long fcount = Zd.ReadArrayLength(t, ref pos);
                    sb.Append($"{pad}@{start} [0xC7] schema({fcount})\n");
                    for (long i = 0; i < fcount; i++)
                    {
                        if (pos >= t.Length) { sb.Append($"{pad}@{pos} <schema EOF>\n"); return; }
                        string fn = ReadStringValue(t, ref pos);
                        string ft = pos < t.Length ? ReadStringValue(t, ref pos) : "";
                        sb.Append($"{pad}  field \"{fn}\": {ft}\n");
                    }
                    return;
                }
            case Zd.TagColumnar:
                {
                    sb.Append($"{pad}@{start} [0xD6] columnar\n");
                    if (pos >= t.Length) { sb.Append($"{pad}@{pos} <columnar EOF>\n"); return; }
                    byte cc = t[pos];
                    if (cc > 0x7F)
                    {
                        sb.Append($"{pad}@{start} [0xD6] columnar 列数非 fixint（定宽），跳过正文\n");
                        return;
                    }
                    pos++;
                    int ncols = cc;
                    var lens = new long[ncols];
                    for (int i = 0; i < ncols; i++)
                    {
                        string name = ReadStringValue(t, ref pos);
                        string type = ReadStringValue(t, ref pos);
                        if (pos >= t.Length || t[pos] > 0x7F)
                        {
                            sb.Append($"{pad}@{pos} <columnar 列长非法>\n");
                            return;
                        }
                        long ln = t[pos++];
                        lens[i] = ln;
                        sb.Append($"{pad}  col[{i}] \"{name}\" ({type}) x{ln}\n");
                    }
                    for (int i = 0; i < ncols; i++)
                        for (long j = 0; j < lens[i]; j++)
                            DumpValue(sb, t, ref pos, depth + 1);
                    return;
                }
            case Zd.TagExt:
                {
                    long tt = Zd.ReadVarint(t, ref pos);
                    long el = Zd.ReadVarint(t, ref pos);
                    if (el < 0 || el > int.MaxValue || pos + el > t.Length)
                    {
                        sb.Append($"{pad}@{start} [0xD7] ext#{tt} 长度越界\n");
                        return;
                    }
                    sb.Append($"{pad}@{start} [0xD7] ext#{tt} {el}B\n");
                    pos += (int)el;
                    return;
                }
            case Zd.TagStringRef:
                {
                    long ri = Zd.ReadVarint(t, ref pos);
                    sb.Append($"{pad}@{start} [0xD8] str-ref #{ri}\n");
                    return;
                }
            case Zd.TagHash:
                {
                    if (pos >= t.Length) { sb.Append($"{pad}@{start} [0xC1] hash <EOF>\n"); return; }
                    byte algo = t[pos++];
                    try
                    {
                        byte[] hb = Zd.DecodeBytes(t, ref pos);
                        sb.Append($"{pad}@{start} [0xC1] content-hash algo={algo} {HexRange(hb, 0, hb.Length)}\n");
                    }
                    catch (ZdFormatException)
                    {
                        sb.Append($"{pad}@{start} [0xC1] content-hash algo={algo} <载荷非法>\n");
                    }
                    return;
                }
            default:
                sb.Append($"{pad}@{start} [0x{tag:X2}] 未知标签\n");
                return;
        }
    }

    private static void DumpArray(StringBuilder sb, byte[] t, ref int pos, int count, int start, int depth, string pad)
    {
        sb.Append($"{pad}@{start} array({count})\n");
        for (int i = 0; i < count; i++)
            DumpValue(sb, t, ref pos, depth + 1);
    }

    private static void DumpMap(StringBuilder sb, byte[] t, ref int pos, int count, int start, int depth, string pad)
    {
        sb.Append($"{pad}@{start} map({count})\n");
        for (int i = 0; i < count; i++)
        {
            int ks = pos;
            // 键必须是字符串；先读 tag 决定长度
            if (pos >= t.Length) { sb.Append($"{pad}@{ks} <map 键 EOF>\n"); return; }
            byte tag = t[pos];
            int klen;
            if (tag >= 0xA0 && tag <= 0xBF) { pos++; klen = tag - 0xA0; }
            else if (tag == Zd.TagStr8) { pos++; klen = ReadByte(t, ref pos, ks); }
            else if (tag == Zd.TagStr16) { klen = ReadU16(t, ref pos, ks); }
            else if (tag == Zd.TagStr32) { klen = (int)ReadU32(t, ref pos, ks); }
            else { sb.Append($"{pad}@{ks} [0x{tag:X2}] map 键非字符串\n"); pos++; continue; }
            string key = ReadUtf8(t, ref pos, klen, ks);
            sb.Append($"{pad}@{ks} key \"{key}\":\n");
            DumpValue(sb, t, ref pos, depth + 1);
        }
    }

    private static void DumpString(StringBuilder sb, byte[] t, ref int pos, long byteLen, int start, string pad)
    {
        if (byteLen < 0 || pos + byteLen > t.Length)
        {
            sb.Append($"{pad}@{start} [str] 长度越界\n");
            return;
        }
        string s = ReadUtf8(t, ref pos, byteLen, start);
        sb.Append($"{pad}@{start} [str {byteLen}B] \"{s}\"\n");
    }

    private static string ReadUtf8(byte[] t, ref int pos, long len, int start)
    {
        if (len < 0 || pos + len > t.Length)
            throw new ZdFormatException("字符串长度越界", start);
        var bytes = new byte[len];
        System.Array.Copy(t, pos, bytes, 0, len);
        pos += (int)len;
        return Encoding.UTF8.GetString(bytes);
    }

    private static string SafeChar(int cp) =>
        (cp < 0x20 || cp == 0x7F || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) ? "." : char.ConvertFromUtf32(cp);

    // ---- 读取辅助（大端，越界抛 ZdFormatException）----

    private static string HexRange(byte[] t, int off, int len)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < len; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(t[off + i].ToString("X2"));
        }
        return sb.ToString();
    }

    private static string ReadStringValue(byte[] t, ref int pos)
    {
        int start = pos;
        if (pos >= t.Length)
            throw new ZdFormatException("字符串缺少标签", start);
        byte tag = t[pos++];
        long len;
        if (tag >= 0xA0 && tag <= 0xBF) len = tag - 0xA0;
        else if (tag == Zd.TagStr8)
        {
            if (pos >= t.Length) throw new ZdFormatException("str8 长度缺失", start);
            len = t[pos++];
        }
        else if (tag == Zd.TagStr16)
        {
            if (pos + 2 > t.Length) throw new ZdFormatException("str16 越界", start);
            len = (t[pos] << 8) | t[pos + 1]; pos += 2;
        }
        else if (tag == Zd.TagStr32)
        {
            if (pos + 4 > t.Length) throw new ZdFormatException("str32 越界", start);
            len = ((long)t[pos] << 24) | ((long)t[pos + 1] << 16) | ((long)t[pos + 2] << 8) | t[pos + 3]; pos += 4;
        }
        else
        {
            throw new ZdFormatException($"期望字符串，实际 0x{tag:X2}", start);
        }
        if (len < 0 || len > int.MaxValue || pos + len > t.Length)
            throw new ZdFormatException("字符串长度越界", start);
        string s = ReadUtf8(t, ref pos, len, start);
        return s;
    }

    private static byte ReadByte(byte[] t, ref int pos, int start)
    {
        if (pos >= t.Length)
            throw new ZdFormatException("字节不足", start);
        return t[pos++];
    }

    private static ushort ReadU16(byte[] t, ref int pos, int start)
    {
        if (pos + 2 > t.Length)
            throw new ZdFormatException("定宽整数越界", start);
        ushort v = (ushort)((t[pos] << 8) | t[pos + 1]);
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
        if (pos + 8 > t.Length)
            throw new ZdFormatException("定宽整数越界", start);
        ulong v = 0;
        for (int i = 0; i < 8; i++)
            v = (v << 8) | t[pos + i];
        pos += 8;
        return v;
    }
}
