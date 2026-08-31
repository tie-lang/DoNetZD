using System.Text;

namespace DoNetZD;

/// <summary>
/// tie:zd 二进制序列化的 .NET 复刻（原语层，对齐 tieDB/persist/zd.tie 的 namespace zd）。
/// 每值 = 类型标签 + 值字节。字节布局与 tie 侧逐字节一致，保证跨语言互通。
/// 目标框架 netstandard2.0（兼容 .NET Framework / .NET Core / .NET 全系）。
/// 格式速查（详见 docs/2026-08-24-donetzd-design.md §3）：
///   整数 fixint 0x00-0x7f（正）/0xe0-0xff（负）；定宽 0xcc u8 /0xcd u16 /0xce u32 /
///   0xcf u64 /0xd0 i8 /0xd1 i16 /0xd2 i32 /0xd3 i64（均大端）；
///   浮点 0xca f32 /0xcb f64（大端）；布尔 0xc2 /0xc3；
///   字符串 0xa0-0xbf fixstr /0xd9 str8 /0xda str16 /0xdb str32（长度=UTF-8 字节数）；
///   数组 0x90-0x9f fixarray /0xdc array16 /0xdd array32；map 0x80-0x8f /0xde /0xdf；
///   tie 扩展 0xc4 char(i32 BE) /0xc5 trit(i8) /0xc6 tuple；record 字段 = varint tag。
///   varint 为 Protobuf 7 位分组、0x80 续位；文件魔数头 8 字节 "TIEDBZD"+v1。
/// </summary>
/// <summary>检测到的 zd 头版本。</summary>
public enum ZdVersion
{
    /// <summary>非法 / 非 zd 数据。</summary>
    Unknown = 0,
    /// <summary>v1（8 字节头："TIEDBZD" + 0x01），仅读取兼容。</summary>
    V1 = 1,
    /// <summary>v2（10 字节头：魔数 9 + flags 1）。</summary>
    V2 = 2,
}

public static class Zd
{
    // ---- 类型标签 ----
    internal const byte TagFixIntPos = 0x00;               // 0x00-0x7F 正 fixint
    internal const byte TagFixIntNeg = 0xE0;               // 0xE0-0xFF 负 fixint
    internal const byte TagMapMin = 0x80;                  // 0x80-0x8F fixmap
    internal const byte TagFixArrayMin = 0x90;             // 0x90-0x9F fixarray
    internal const byte TagFixStrMin = 0xA0;               // 0xA0-0xBF fixstr
    internal const byte TagNull = 0xC0;                    // v2 null/空值/缺失
    internal const byte TagFalse = 0xC2;
    internal const byte TagTrue = 0xC3;
    internal const byte TagChar = 0xC4;
    internal const byte TagTrit = 0xC5;
    internal const byte TagBytes = 0xC6;                   // v2 bytes（v1 中 0xC6 旧义为 tuple，v1 从不落盘）
    internal const byte TagTuple = 0xC6;                   // v1 旧义（标签复用，见 TagBytes）
    internal const byte TagF32 = 0xCA;
    internal const byte TagF64 = 0xCB;
    internal const byte TagU8 = 0xCC;
    internal const byte TagU16 = 0xCD;
    internal const byte TagU32 = 0xCE;
    internal const byte TagU64 = 0xCF;
    internal const byte TagI8 = 0xD0;
    internal const byte TagI16 = 0xD1;
    internal const byte TagI32 = 0xD2;
    internal const byte TagI64 = 0xD3;
    internal const byte TagExt = 0xD7;                     // v2 扩展类型
    internal const byte TagStr8 = 0xD9;
    internal const byte TagStr16 = 0xDA;
    internal const byte TagStr32 = 0xDB;
    internal const byte TagArray16 = 0xDC;
    internal const byte TagArray32 = 0xDD;
    internal const byte TagMap16 = 0xDE;
    internal const byte TagMap32 = 0xDF;

    // ---- 文件魔数头 ----
    /// <summary>前 7 字节魔数 "TIEDBZD"（版本字节前缀）。</summary>
    internal static readonly byte[] MagicPrefix = { 0x54, 0x49, 0x45, 0x44, 0x42, 0x5A, 0x44 };

    /// <summary>v1 头（8 字节）："TIEDBZD" + 版本 0x01。仅读取兼容。</summary>
    internal static readonly byte[] MagicV1 = { 0x54, 0x49, 0x45, 0x44, 0x42, 0x5A, 0x44, 0x01 };

    /// <summary>v2 头（9 字节）："TIEDBZD" + base48 2 位版本 "02" = [0][2]。不含 flags 字节。</summary>
    internal static readonly byte[] MagicV2 = { 0x54, 0x49, 0x45, 0x44, 0x42, 0x5A, 0x44, 0x00, 0x02 };

    /// <summary>当前活动魔数 = v2（9 字节，不含 flags 字节）。</summary>
    internal static readonly byte[] Magic = MagicV2;

    /// <summary>v1 头长（字节）。</summary>
    internal const int V1HeaderLength = 8;
    /// <summary>v2 头长（字节）＝ 魔数 9 + flags 1。</summary>
    internal const int V2HeaderLength = 10;

    // ---- v2 flags 字节位（位于 v2 头第 10 字节，bit0-bit4 收敛自规范 §2，bit5-6 扩展）----
    /// <summary>字符串字典/引用位（bit0，值 1）。</summary>
    public const byte FlagDict = 1;
    /// <summary>列式容器位（bit1，值 2）。</summary>
    public const byte FlagColumnar = 2;
    /// <summary>扩展类型位（bit2，值 4）。</summary>
    public const byte FlagExt = 4;
    /// <summary>流式分块位（bit3，值 8）。</summary>
    public const byte FlagStream = 8;
    /// <summary>压缩变体位（bit4，值 16）。</summary>
    public const byte FlagCompressed = 16;
    /// <summary>Schema 段位（bit5，值 32，扩展）。</summary>
    public const byte FlagSchema = 32;
    /// <summary>内容哈希段位（bit6，值 64，扩展）。</summary>
    public const byte FlagHash = 64;

    /// <summary>文件魔数头副本（"TIEDBZD"+base48"02"，9 字节）。</summary>
    public static byte[] MagicHeader => (byte[])Magic.Clone();

    /// <summary>v1 头副本（"TIEDBZD"+0x01，8 字节），仅供写兼容测试/构造 v1 文件。</summary>
    public static byte[] V1Header => (byte[])MagicV1.Clone();

    /// <summary>构造 v2 完整文件头（魔数 9 + flags 1，共 10 字节）。</summary>
    public static byte[] V2Header(byte flags)
    {
        var h = new byte[V2HeaderLength];
        Buffer.BlockCopy(MagicV2, 0, h, 0, MagicV2.Length);
        h[MagicV2.Length] = flags;
        return h;
    }

    // ==================== 字节工具 ====================

    /// <summary>拼接两个字节表（a 在前，b 在后）。对齐 zd.concat（byte_concat）。</summary>
    public static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }

    internal static byte[] ConcatOne(byte tag, byte[] rest)
        => Concat(new[] { tag }, rest);

    /// <summary>u16/u32/u64 大端（高字节在前），跨平台与平台字节序无关。</summary>
    internal static byte[] WriteU16Be(ushort n) => new[] { (byte)(n >> 8), (byte)n };
    internal static byte[] WriteU32Be(uint n) => new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n };
    internal static byte[] WriteU64Be(ulong n) => new[]
    {
        (byte)(n >> 56), (byte)(n >> 48), (byte)(n >> 40), (byte)(n >> 32),
        (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n,
    };

    // ---- 浮点大端字节（BitConverter.GetBytes + 端序校正）----

    /// <summary>double → 8 字节大端 IEEE 754。平台无关。</summary>
    internal static byte[] WriteF64Be(double x)
    {
        byte[] b = BitConverter.GetBytes(x);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(b);
        return b;
    }

    /// <summary>float → 4 字节大端 IEEE 754。平台无关。</summary>
    internal static byte[] WriteF32Be(float x)
    {
        byte[] b = BitConverter.GetBytes(x);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(b);
        return b;
    }

    // ==================== varint（Protobuf 7 位分组）====================

    /// <summary>变长编码；n&lt;0 返回空（对齐 zd.write_varint）。</summary>
    public static byte[] WriteVarint(long n)
    {
        if (n < 0)
            return Array.Empty<byte>();
        ulong v = (ulong)n;
        var outBytes = new byte[1];
        int len = 0;
        while (v >= 0x80)
        {
            if (len >= outBytes.Length)
                Array.Resize(ref outBytes, outBytes.Length * 2);
            outBytes[len++] = (byte)((v & 0x7F) | 0x80);
            v >>= 7;
        }
        if (len >= outBytes.Length)
            Array.Resize(ref outBytes, outBytes.Length * 2);
        outBytes[len++] = (byte)v;
        Array.Resize(ref outBytes, len);
        return outBytes;
    }

    // ==================== 标量编码 ====================

    /// <summary>整数编码：MessagePack 分区，最紧凑优先（对齐 zd.encode_i64）。</summary>
    public static byte[] EncodeI64(long n)
    {
        if (n >= 0 && n <= 127)
            return new[] { (byte)n };                                        // fixint 正
        if (n >= -32 && n < 0)
            return new[] { (byte)(n & 0xFF) };                               // fixint 负
        if (n >= -128 && n < -32)
            return new[] { TagI8, (byte)(n & 0xFF) };                        // i8
        if (n >= 128 && n <= 255)
            return new[] { TagU8, (byte)n };                                 // u8
        if (n >= -32768 && n < -128)
            return ConcatOne(TagI16, WriteU16Be((ushort)(n & 0xFFFF)));      // i16
        if (n >= 256 && n <= 65535)
            return ConcatOne(TagU16, WriteU16Be((ushort)n));                 // u16
        if (n >= int.MinValue && n < -32768)
            return ConcatOne(TagI32, WriteU32Be((uint)(n & 0xFFFF_FFFF)));   // i32
        if (n >= 65536 && n <= uint.MaxValue)
            return ConcatOne(TagU32, WriteU32Be((uint)n));                   // u32
        // 剩余：非负大值 → 0xcf u64；负大值 → 0xd3 i64
        if (n >= 0)
            return ConcatOne(TagU64, WriteU64Be((ulong)n));                  // u64
        return ConcatOne(TagI64, WriteU64Be((ulong)n));                      // i64
    }

    /// <summary>f64 编码：0xcb + 8 字节大端 IEEE 754。</summary>
    public static byte[] EncodeF64(double x) => ConcatOne(TagF64, WriteF64Be(x));

    /// <summary>f32 编码：0xca + 4 字节大端 IEEE 754。</summary>
    public static byte[] EncodeF32(float x) => ConcatOne(TagF32, WriteF32Be(x));

    /// <summary>bool 编码：0xc2 false / 0xc3 true。</summary>
    public static byte[] EncodeBool(bool b) => new[] { b ? TagTrue : TagFalse };

    /// <summary>char（单字符字符串）编码：0xc4 + i32 码点大端 4 字节；空串/码点&lt;0 按 0（对齐 zd）。</summary>
    public static byte[] EncodeChar(string c)
    {
        int cp = GetCodepoint(c);
        if (cp < 0)
            cp = 0;
        return ConcatOne(TagChar, WriteU32Be((uint)cp));
    }

    private static int GetCodepoint(string c)
    {
        if (string.IsNullOrEmpty(c))
            return 0;
        // 取首个码点：单字符直取；头元素为高代理时跨代理对取完整码点，否则取首字符码值
        return char.ConvertToUtf32(c, 0);
    }

    /// <summary>trit（-1/0/1 三值）编码：0xc5 + i8 1 字节。</summary>
    public static byte[] EncodeTrit(long v) => new[] { TagTrit, (byte)(v & 0xFF) };

    /// <summary>字符串编码：长度前缀按 UTF-8 字节数选择（fixstr/str8/str16/str32）+ UTF-8 字节。</summary>
    public static byte[] EncodeString(string s)
    {
        byte[] body = Encoding.UTF8.GetBytes(s ?? string.Empty);
        int n = body.Length;
        if (n <= 31)
            return ConcatOne((byte)(TagFixStrMin | n), body);
        if (n <= 255)
        {
            var head = new byte[] { TagStr8, (byte)n };
            return Concat(head, body);
        }
        if (n <= 65535)
            return Concat(Concat(new[] { TagStr16 }, WriteU16Be((ushort)n)), body);
        return Concat(Concat(new[] { TagStr32 }, WriteU32Be((uint)n)), body);
    }

    // ==================== v2 类型原语：null / bytes / ext ====================

    /// <summary>null 编码：0xc0（v2）。</summary>
    public static byte[] EncodeNull() => new[] { TagNull };

    /// <summary>bytes 编码：0xc6 + 数组头(长度) + 原始字节（v2）。</summary>
    public static byte[] EncodeBytes(byte[] data)
    {
        byte[] raw = data ?? Array.Empty<byte>();
        return Concat(ConcatOne(TagBytes, EncodeArrayHeader(raw.Length)), raw);
    }

    /// <summary>ext 编码：0xd7 + varint(typeTag) + varint(len) + 载荷（v2）。typeTag 须非负。</summary>
    public static byte[] EncodeExt(long typeTag, byte[] payload)
    {
        if (typeTag < 0)
            throw new ArgumentOutOfRangeException(nameof(typeTag), "zd v2 ext 类型标记须非负（varint 编码）。");
        byte[] p = payload ?? Array.Empty<byte>();
        byte[] head = ConcatOne(TagExt, WriteVarint(typeTag));
        head = Concat(head, WriteVarint(p.Length));
        return Concat(head, p);
    }

    /// <summary>读一个 varint（7 位分组、0x80 续位），并推进 pos；字节不足抛异常。</summary>
    public static long ReadVarint(byte[] t, ref int pos)
    {
        ulong v = 0;
        int shift = 0;
        while (true)
        {
            if (t is null || pos >= t.Length)
                throw new ZdFormatException("varint 不完整（提前结束）", pos);
            byte b = t[pos++];
            v |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                break;
            shift += 7;
            if (shift > 63)
                throw new ZdFormatException("varint 过长", pos);
        }
        return (long)v;
    }

    /// <summary>读数组头返回长度（fixarray/array16/array32），并推进 pos；非数组头抛异常。</summary>
    public static long ReadArrayLength(byte[] t, ref int pos)
    {
        if (t is null || pos >= t.Length)
            throw new ZdFormatException("array 头缺失", pos);
        byte tag = t[pos];
        if (tag >= 0x90 && tag <= 0x9F) { pos++; return tag - 0x90; }
        if (tag == TagArray16 || tag == TagArray32)
        {
            pos++;
            if (tag == TagArray16)
            {
                if (pos + 2 > t.Length)
                    throw new ZdFormatException("array16 长度越界", pos);
                long v = (t[pos] << 8) | t[pos + 1];
                pos += 2;
                return v;
            }
            if (pos + 4 > t.Length)
                throw new ZdFormatException("array32 长度越界", pos);
            long u = ((long)t[pos] << 24) | ((long)t[pos + 1] << 16) | ((long)t[pos + 2] << 8) | t[pos + 3];
            pos += 4;
            return u;
        }
        throw new ZdFormatException($"期望数组头，实际 0x{tag:X2}", pos);
    }

    /// <summary>null 原语解码：期望 0xc0（pos 指向标签），消费 1 字节。</summary>
    public static void DecodeNull(byte[] t, ref int pos)
    {
        if (t is null || pos >= t.Length || t[pos] != TagNull)
            throw new ZdFormatException($"期望 null(0xc0)，实际 0x{(t is null || pos >= t.Length ? "EOF" : t[pos].ToString("X2"))}", pos);
        pos++;
    }

    /// <summary>bytes 原语解码：0xc6 + 数组头(长度) + 原始字节。</summary>
    public static byte[] DecodeBytes(byte[] t, ref int pos)
    {
        int start = pos;
        if (t is null || pos >= t.Length || t[pos] != TagBytes)
            throw new ZdFormatException($"期望 bytes(0xc6)，实际 0x{(t is null || pos >= t.Length ? "EOF" : t[pos].ToString("X2"))}", start);
        pos++;
        long len = ReadArrayLength(t, ref pos);
        if (len < 0 || len > int.MaxValue || pos + len > t.Length)
            throw new ZdFormatException("bytes 长度越界", start);
        byte[] raw = new byte[len];
        Array.Copy(t, pos, raw, 0, (int)len);
        pos += (int)len;
        return raw;
    }

    /// <summary>ext 原语解码：0xd7 + varint(typeTag) + varint(len) + 载荷。</summary>
    public static void DecodeExt(byte[] t, ref int pos, out long typeTag, out byte[] payload)
    {
        int start = pos;
        typeTag = 0;
        payload = Array.Empty<byte>();
        if (t is null || pos >= t.Length || t[pos] != TagExt)
            throw new ZdFormatException($"期望 ext(0xd7)，实际 0x{(t is null || pos >= t.Length ? "EOF" : t[pos].ToString("X2"))}", start);
        pos++;
        typeTag = ReadVarint(t, ref pos);
        long len = ReadVarint(t, ref pos);
        if (len < 0 || len > int.MaxValue || pos + len > t.Length)
            throw new ZdFormatException("ext 载荷长度越界", start);
        payload = new byte[len];
        Array.Copy(t, pos, payload, 0, (int)len);
        pos += (int)len;
    }

    // ==================== 容器头 ====================

    /// <summary>数组头：fixarray(≤15)/array16(≤65535)/array32。</summary>
    public static byte[] EncodeArrayHeader(int count)
    {
        if (count <= 15)
            return new[] { (byte)(TagFixArrayMin | count) };
        if (count <= 65535)
            return ConcatOne(TagArray16, WriteU16Be((ushort)count));
        return ConcatOne(TagArray32, WriteU32Be((uint)count));
    }

    /// <summary>map 头：fixmap(≤15)/map16(≤65535)/map32。</summary>
    public static byte[] EncodeMapHeader(int count)
    {
        if (count <= 15)
            return new[] { (byte)(TagMapMin | count) };
        if (count <= 65535)
            return ConcatOne(TagMap16, WriteU16Be((ushort)count));
        return ConcatOne(TagMap32, WriteU32Be((uint)count));
    }

    /// <summary>元组 = 数组编码（复用数组头）。</summary>
    public static byte[] EncodeTupleHeader(int count) => EncodeArrayHeader(count);

    /// <summary>record 字段标签：varint(field_number&lt;&lt;3 | wire_type)。</summary>
    public static byte[] EncodeRecordFieldTag(int fieldNumber, int wireType) => WriteVarint(fieldNumber << 3 | wireType);

    // ==================== 文件魔数 / 版本识别 ====================

    /// <summary>前 7 字节是否匹配魔数 "TIEDBZD"（从 off 起）。</summary>
    internal static bool MagicPrefixMatch(byte[] b, int off)
    {
        if (b is null || b.Length < off + MagicPrefix.Length)
            return false;
        for (int i = 0; i < MagicPrefix.Length; i++)
            if (b[off + i] != MagicPrefix[i])
                return false;
        return true;
    }

    /// <summary>base48 版本位是否有效（字节值 0..47，即字符集 '0'..'L' 的下标）。</summary>
    internal static bool IsValidVersionByte(byte b) => b < 48;

    /// <summary>
    /// 检测字节流携带的 zd 头版本。
    /// v1 = 8 字节头（7 魔数 + 版本 0x01）；v2 = 9 字节魔数（7 魔数 + 2 位 base48 版本）且
    /// 版本字节 ∈ [0,47]；两者都仅在前 7 字节匹配 "TIEDBZD" 时判定，其余 Unknown。
    /// <para>先判 v1（其版本位固定 0x01，签名最特异），避免把 v1 文件正文当作 v2 版本位。</para>
    /// </summary>
    public static ZdVersion DetectVersion(byte[] data)
    {
        if (data is null || data.Length < 8 || !MagicPrefixMatch(data, 0))
            return ZdVersion.Unknown;
        if (data[7] == 0x01)
            return ZdVersion.V1;                 // 版本位 0x01 → v1（最特异签名，优先）
        if (data.Length >= 9 && IsValidVersionByte(data[7]) && IsValidVersionByte(data[8]))
            return ZdVersion.V2;                 // 两字节 base48 版本 → v2
        return ZdVersion.Unknown;
    }

    /// <summary>从 v2 头提取 flags 字节；非 v2 返回 0。</summary>
    public static byte GetFlags(byte[] data)
    {
        if (DetectVersion(data) != ZdVersion.V2 || data.Length < V2HeaderLength)
            return 0;
        return data[9];
    }

    /// <summary>是否为 v2 头数据。</summary>
    public static bool IsV2(byte[] data) => DetectVersion(data) == ZdVersion.V2;

    /// <summary>校验字节表是否带合法 zd 魔数头（v1 或 v2）。</summary>
    public static bool IsZd(byte[] data) => DetectVersion(data) != ZdVersion.Unknown;

    /// <summary>按版本取头长：v1=8，v2=10，其它 0。</summary>
    internal static int HeaderLength(ZdVersion v) => v switch
    {
        ZdVersion.V1 => V1HeaderLength,
        ZdVersion.V2 => V2HeaderLength,
        _ => 0,
    };

    /// <summary>把带头的 zd 字节去头返回正文；非法返回空数组。</summary>
    internal static byte[] ExtractBody(byte[] data)
    {
        int hl = HeaderLength(DetectVersion(data));
        if (hl <= 0 || data.Length < hl)
            return Array.Empty<byte>();
        var body = new byte[data.Length - hl];
        Buffer.BlockCopy(data, hl, body, 0, body.Length);
        return body;
    }

    /// <summary>把 zd 正文按 v2 头（flags=0）写入文件。返回是否写入成功。</summary>
    public static bool Save(string path, byte[] bytes)
    {
        try
        {
            byte[] header = V2Header(0);
            byte[] body = bytes ?? Array.Empty<byte>();
            var file = new byte[header.Length + body.Length];
            Buffer.BlockCopy(header, 0, file, 0, header.Length);
            Buffer.BlockCopy(body, 0, file, header.Length, body.Length);
            File.WriteAllBytes(path, file);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读 zd 文件：识别 v1/v2 头并去头返回正文；非 zd 文件返回空数组。</summary>
    public static byte[] Load(string path)
    {
        try
        {
            return ExtractBody(File.ReadAllBytes(path));
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    // ==================== 异步文件 IO ====================

    /// <summary>异步把 zd 字节（含魔数头）写入文件。失败返回 false。</summary>
    public static async Task<bool> SaveAsync(string path, byte[] bytes)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            byte[] header = V2Header(0);
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
            if (DetectVersion(data) == ZdVersion.Unknown)
                return Array.Empty<byte>();
            return ExtractBody(data);
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    // ==================== CRC32 校验文件（v2 头 + flags + CRC + body）====================
    // 与普通 Save/Load 区别：在 v2 头（10 字节）后多 4 字节大端 CRC32（覆盖 body）。
    // 仅接受 v2 头；CRC 位于偏移 10，body 自偏移 14。

    /// <summary>把 zd 字节 + CRC32 校验写入文件（v2 头 + 4B CRC + body）。</summary>
    public static bool SaveChecked(string path, byte[] bytes)
    {
        try
        {
            byte[] body = bytes ?? Array.Empty<byte>();
            uint crc = ZdCrc32.Compute(body);
            byte[] file = Concat(V2Header(0), Concat(WriteU32Be(crc), body));
            File.WriteAllBytes(path, file);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读 CRC 校验 zd 文件并验证；失败抛 <see cref="InvalidDataException"/>。</summary>
    public static byte[] LoadChecked(string path)
    {
        if (!TryLoadChecked(path, out byte[]? body, out string? error))
            throw new InvalidDataException(error);
        return body!;
    }

    /// <summary>尝试读 CRC 校验 zd 文件；失败返回 false 并带错误信息。</summary>
    public static bool TryLoadChecked(string path, out byte[]? body, out string? error)
    {
        body = null;
        error = null;
        try
        {
            byte[] data = File.ReadAllBytes(path);
            if (DetectVersion(data) != ZdVersion.V2) { error = "非 v2 zd 文件（魔数/版本不匹配）"; return false; }
            if (data.Length < V2HeaderLength + 4) { error = "zd 校验文件过短（缺 CRC）"; return false; }
            uint stored = ReadU32Be(data, 10);
            byte[] payload = new byte[data.Length - V2HeaderLength - 4];
            Buffer.BlockCopy(data, V2HeaderLength + 4, payload, 0, payload.Length);
            uint actual = ZdCrc32.Compute(payload);
            if (stored != actual)
            {
                error = $"CRC 校验失败：期望 0x{stored:X8}，实际 0x{actual:X8}";
                return false;
            }
            body = payload;
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    internal static uint ReadU32Be(byte[] b, int off)
        => (uint)((b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3]);
}