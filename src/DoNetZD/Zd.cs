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
public static class Zd
{
    // ---- 类型标签 ----
    internal const byte TagFixIntPos = 0x00;               // 0x00-0x7F 正 fixint
    internal const byte TagFixIntNeg = 0xE0;               // 0xE0-0xFF 负 fixint
    internal const byte TagMapMin = 0x80;                  // 0x80-0x8F fixmap
    internal const byte TagFixArrayMin = 0x90;             // 0x90-0x9F fixarray
    internal const byte TagFixStrMin = 0xA0;               // 0xA0-0xBF fixstr
    internal const byte TagFalse = 0xC2;
    internal const byte TagTrue = 0xC3;
    internal const byte TagChar = 0xC4;
    internal const byte TagTrit = 0xC5;
    internal const byte TagTuple = 0xC6;
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
    internal const byte TagStr8 = 0xD9;
    internal const byte TagStr16 = 0xDA;
    internal const byte TagStr32 = 0xDB;
    internal const byte TagArray16 = 0xDC;
    internal const byte TagArray32 = 0xDD;
    internal const byte TagMap16 = 0xDE;
    internal const byte TagMap32 = 0xDF;

    // ---- 文件魔数头 "TIEDBZD" + v1 ----
    internal static readonly byte[] Magic = { 0x54, 0x49, 0x45, 0x44, 0x42, 0x5A, 0x44, 0x01 };

    /// <summary>文件魔数头副本（"TIEDBZD"+v1，8 字节）。</summary>
    public static byte[] MagicHeader => (byte[])Magic.Clone();

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

    // ==================== 文件魔数 ====================

    /// <summary>校验字节表是否带 "TIEDBZD"+v1 魔数头（长度≥8 且前 8 字节匹配）。</summary>
    public static bool IsZd(byte[] data)
    {
        if (data == null || data.Length < Magic.Length)
            return false;
        for (int i = 0; i < Magic.Length; i++)
        {
            if (data[i] != Magic[i])
                return false;
        }
        return true;
    }

    /// <summary>把 zd 字节（含魔数头）写入文件。返回是否写入成功。</summary>
    public static bool Save(string path, byte[] bytes)
    {
        try
        {
            byte[] file = Concat(Magic, bytes ?? Array.Empty<byte>());
            File.WriteAllBytes(path, file);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读 zd 文件：校验魔数头、去头返回正文；非 zd 文件返回空数组。</summary>
    public static byte[] Load(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            if (!IsZd(data))
                return Array.Empty<byte>();
            var body = new byte[data.Length - Magic.Length];
            Buffer.BlockCopy(data, Magic.Length, body, 0, body.Length);
            return body;
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }
}