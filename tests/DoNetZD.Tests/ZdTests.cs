using DoNetZD;
using Xunit;

namespace DoNetZD.Tests;

/// <summary>
/// 原语层黄金字节向量：与 tieDB/persist/zd.tie 的字节布局逐字节比对。
/// 这些向量是手工从 tie:zd 定稿格式推导的（见 docs/2026-08-24-donetzd-design.md §3），
/// 作为跨语言互通的第一道护栏。
/// </summary>
public class ZdPrimitiveTests
{
    private static string Hex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));

    private static void AssertBytes(byte[] expected, byte[] actual)
    {
        Assert.Equal(Hex(expected), Hex(actual));
    }

    [Fact]
    public void EncodeI64_AllRanges()
    {
        // fixint 正（0..127）
        AssertBytes([0x05], Zd.EncodeI64(5));
        AssertBytes([0x7F], Zd.EncodeI64(127));
        // fixint 负（-32..-1）
        AssertBytes([0xFF], Zd.EncodeI64(-1));
        AssertBytes([0xE0], Zd.EncodeI64(-32));
        // u8（128..255）
        AssertBytes([0xCC, 0x80], Zd.EncodeI64(128));
        AssertBytes([0xCC, 0xFF], Zd.EncodeI64(255));
        // u16（256..65535）
        AssertBytes([0xCD, 0x01, 0x2C], Zd.EncodeI64(300));
        AssertBytes([0xCD, 0xFF, 0xFF], Zd.EncodeI64(65535));
        // u32（65536..2^32-1）
        AssertBytes([0xCE, 0x00, 0x01, 0x11, 0x70], Zd.EncodeI64(70000));
        AssertBytes([0xCE, 0xFF, 0xFF, 0xFF, 0xFF], Zd.EncodeI64(4294967295));
        // u64（非负大值）
        AssertBytes([0xCF, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00], Zd.EncodeI64(4294967296));
        // i8（-128..-33）
        AssertBytes([0xD0, 0xDF], Zd.EncodeI64(-33));
        AssertBytes([0xD0, 0x80], Zd.EncodeI64(-128));
        // i16（-32768..-129）
        AssertBytes([0xD1, 0x80, 0x00], Zd.EncodeI64(-32768));
        // i32（-2^31..-32769）
        AssertBytes([0xD2, 0x80, 0x00, 0x00, 0x00], Zd.EncodeI64(int.MinValue));
        // i64（负大值）
        AssertBytes([0xD3, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 0xFF, 0xFF, 0xFF], Zd.EncodeI64(-2147483649));
    }

    [Fact]
    public void EncodeScalars()
    {
        AssertBytes([0xC3], Zd.EncodeBool(true));
        AssertBytes([0xC2], Zd.EncodeBool(false));
        AssertBytes([0xCB, 0x3F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], Zd.EncodeF64(1.0));
        AssertBytes([0xCB, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], Zd.EncodeF64(-2.0));
        AssertBytes([0xA2, 0x68, 0x69], Zd.EncodeString("hi"));
        AssertBytes([0xA3, 0xE4, 0xB8, 0xAD], Zd.EncodeString("中"));
        AssertBytes([0xD9, 0x20], Zd.EncodeString(new string('x', 32))[..2]);         // str8
        AssertBytes([0xDA, 0x01, 0x00], Zd.EncodeString(new string('a', 256))[..3]);  // str16
        AssertBytes([0xC4, 0x00, 0x00, 0x00, 0x41], Zd.EncodeChar("A"));              // char
        AssertBytes([0xC5, 0xFF], Zd.EncodeTrit(-1));                                 // trit
        AssertBytes([0xC5, 0x00], Zd.EncodeTrit(0));
        AssertBytes([0xC5, 0x01], Zd.EncodeTrit(1));
    }

    [Fact]
    public void ContainersAndVarint()
    {
        AssertBytes([0x93], Zd.EncodeArrayHeader(3));
        AssertBytes([0xDC, 0x01, 0x00], Zd.EncodeArrayHeader(256));
        AssertBytes([0x92], Zd.EncodeTupleHeader(2));                                  // tuple = 数组编码
        AssertBytes([0x81], Zd.EncodeMapHeader(1));
        AssertBytes([0xDE, 0x00, 0x40], Zd.EncodeMapHeader(64));
        AssertBytes([0xAC, 0x02], Zd.WriteVarint(300));                                // 300 = 0x02AC → 7位分组
        AssertBytes([0x7F], Zd.WriteVarint(127));
        AssertBytes(Array.Empty<byte>(), Zd.WriteVarint(-1));                          // 负 → 空
    }

    [Fact]
    public void MagicHeader_V2()
    {
        // 当前活动魔数 = v2（9 字节："TIEDBZD" + base48 "02"）
        AssertBytes([0x54, 0x49, 0x45, 0x44, 0x42, 0x5A, 0x44, 0x00, 0x02], Zd.MagicHeader);

        byte[] body = Zd.EncodeI64(42);
        byte[] file = Zd.Concat(Zd.MagicHeader, body);
        Assert.True(Zd.IsZd(file));
        Assert.Equal(ZdVersion.V2, Zd.DetectVersion(Zd.Concat(Zd.V2Header(0), body)));
        Assert.Equal(ZdVersion.V2, Zd.DetectVersion(Zd.Concat(Zd.MagicHeader, body)));
        Assert.False(Zd.IsZd(new byte[] { 0x01, 0x02, 0x03 }));
        Assert.False(Zd.IsZd(Array.Empty<byte>()));
    }

    [Fact]
    public void DetectVersion_V1Compatible()
    {
        // v1（8 字节头，"TIEDBZD" + 0x01）仍可识别
        byte[] v1 = [0x54, 0x49, 0x45, 0x44, 0x42, 0x5A, 0x44, 0x01];
        byte[] v1Body = Zd.EncodeString("hello");
        byte[] v1File = Zd.Concat(v1, v1Body);
        Assert.Equal(ZdVersion.V1, Zd.DetectVersion(v1File));
        Assert.True(Zd.IsZd(v1File));
        Assert.False(Zd.IsV2(v1File));
        Assert.Equal(0, Zd.GetFlags(v1File));

        // v2 头识别
        byte[] v2File = Zd.Concat(Zd.V2Header(Zd.FlagDict | Zd.FlagColumnar), Zd.EncodeI64(7));
        Assert.Equal(ZdVersion.V2, Zd.DetectVersion(v2File));
        Assert.True(Zd.IsV2(v2File));
        Assert.Equal(Zd.FlagDict | Zd.FlagColumnar, (byte)(Zd.GetFlags(v2File) & (Zd.FlagDict | Zd.FlagColumnar)));

        // 非法魔数 / 非法版本字节
        Assert.Equal(ZdVersion.Unknown, Zd.DetectVersion([0x54, 0x49, 0x45, 0x44, 0x42, 0x5A, 0x44, 0x7F]));
        Assert.Equal(ZdVersion.Unknown, Zd.DetectVersion([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]));
    }
}

/// <summary>类型化层：Encode → Decode 回环 + 便捷对象映射。</summary>
public class ZdCodecTests
{
    [Fact]
    public void Roundtrip_Scalars()
    {
        AssertRoundtrip(new ZdValue.Integer(42));
        AssertRoundtrip(new ZdValue.Integer(-100000));
        AssertRoundtrip(new ZdValue.Float(3.14));
        AssertRoundtrip(new ZdValue.Bool(true));
        AssertRoundtrip(new ZdValue.Bool(false));
        AssertRoundtrip(new ZdValue.String("你好，tie:zd"));
        AssertRoundtrip(new ZdValue.Char(0x4E2D));
        AssertRoundtrip(new ZdValue.Trit(-1));
    }

    [Fact]
    public void Roundtrip_Nested()
    {
        var root = new ZdValue.Map(new Dictionary<string, ZdValue>
        {
            ["width"] = new ZdValue.Integer(1920),
            ["name"] = new ZdValue.String("照片"),
            ["ok"] = new ZdValue.Bool(true),
            ["list"] = new ZdValue.Array(new ZdValue[]
            {
                new ZdValue.Integer(1),
                new ZdValue.Float(2.5),
                new ZdValue.String("三"),
            }),
        });
        AssertRoundtrip(root);
    }

    [Fact]
    public void FromObject_MapsClrTypes()
    {
        byte[] enc = ZdCodec.Encode(ZdValue.FromObject(new Dictionary<string, object?>
        {
            ["id"] = 123,
            ["ratio"] = 0.5,
            ["tag"] = "fpter",
            ["flags"] = new object?[] { true, false, 7 },
        }));
        var dec = ZdCodec.Decode(enc) as ZdValue.Map;
        Assert.NotNull(dec);
        Assert.Equal(123L, ((ZdValue.Integer)dec.Entries["id"]).Value);
        Assert.Equal(0.5, ((ZdValue.Float)dec.Entries["ratio"]).Value);
        Assert.Equal("fpter", ((ZdValue.String)dec.Entries["tag"]).Value);
        var arr = (ZdValue.Array)dec.Entries["flags"];
        Assert.Equal(3, arr.Items.Count);
    }

    [Fact]
    public void Decode_BadTag_Throws()
    {
        var ex = Assert.Throws<ZdFormatException>(() => ZdCodec.Decode(new byte[] { 0xCC })); // u8 却缺数据
        Assert.Contains("不完整", ex.Message);
    }

    private static void AssertRoundtrip(ZdValue value)
    {
        byte[] bytes = ZdCodec.Encode(value);
        ZdValue back = ZdCodec.Decode(bytes);
        Assert.Equal(value.ToString(), back.ToString());
    }
}

/// <summary>文件层：Save/Load 魔数头往返。</summary>
public class ZdFileTests
{
    [Fact]
    public void SaveLoad_Roundtrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"zd_{Guid.NewGuid():N}.zd");
        try
        {
            byte[] body = ZdCodec.Encode(new ZdValue.Map(new Dictionary<string, ZdValue>
            {
                ["v"] = new ZdValue.Integer(99),
                ["s"] = new ZdValue.String("中文"),
            }));

            Assert.True(Zd.Save(path, body));
            Assert.True(Zd.IsZd(File.ReadAllBytes(path)));

            byte[] loaded = Zd.Load(path);
            Assert.Equal(Hex(body), Hex(loaded));

            // 用错误的魔数文件 Load → 空
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            Assert.Empty(Zd.Load(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static string Hex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));
}