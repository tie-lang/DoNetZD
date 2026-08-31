using DoNetZD;
using Xunit;

namespace DoNetZD.Tests;

/// <summary>zd v2 新增能力：字符串字典/引用、列式、schema、内容哈希（golden 向量 + 回环）。</summary>
public class V2FeaturesTests
{
    private static string Hex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));

    private static void AssertBytes(byte[] expected, byte[] actual) => Assert.Equal(Hex(expected), Hex(actual));

    // ==================== golden 字节向量 ====================

    [Fact]
    public void Golden_V2Header()
    {
        // 10 字节 v2 头：魔数 7 + base48 "02" + flags 0
        AssertBytes([0x54, 0x49, 0x45, 0x44, 0x42, 0x5A, 0x44, 0x00, 0x02, 0x00], Zd.V2Header(0));
        // flags 位：字典 bit0=1 | 列式 bit1=2 | ext bit2=4 | 流 bit3=8 | 压缩 bit4=16
        AssertBytes(
            [0x54, 0x49, 0x45, 0x44, 0x42, 0x5A, 0x44, 0x00, 0x02, 0x1F],
            Zd.V2Header(Zd.FlagDict | Zd.FlagColumnar | Zd.FlagExt | Zd.FlagStream | Zd.FlagCompressed));
    }

    [Fact]
    public void Golden_NullBytesExt()
    {
        AssertBytes([0xC0], Zd.EncodeNull());

        // bytes = 0xC6 + 数组头(长度) + 原始字节（4 字节 → fixarray 0x94）
        AssertBytes([0xC6, 0x94, 0xDE, 0xAD, 0xBE, 0xEF], Zd.EncodeBytes([0xDE, 0xAD, 0xBE, 0xEF]));
        AssertBytes([0xC6, 0x90], Zd.EncodeBytes([]));   // 空 bytes = 0xC6 + fixarray(0)

        // ext = 0xD7 + varint(typeTag) + varint(len) + 载荷
        AssertBytes([0xD7, 0x02, 0x02, 0x01, 0x02], Zd.EncodeExt(2, [0x01, 0x02]));

        // 解码原语回环
        int p = 0;
        byte[] b = [0xC6, 0x94, 0xDE, 0xAD, 0xBE, 0xEF];
        AssertBytes([0xDE, 0xAD, 0xBE, 0xEF], Zd.DecodeBytes(b, ref p));
        Assert.Equal(6, p);

        int q = 0;
        Zd.DecodeExt([0xD7, 0x02, 0x02, 0x01, 0x02], ref q, out long typeTag, out byte[] payload);
        Assert.Equal(2, typeTag);
        AssertBytes([0x01, 0x02], payload);
    }

    // ==================== 字符串字典 / 引用 ====================

    [Fact]
    public void StringDict_Roundtrip_Dedups()
    {
        // name 在多个 record 中重复出现 → 池段只存一次，正文用 0xd8 引用
        var rows = new ZdValue.Array(new ZdValue[]
        {
            new ZdValue.Map(new Dictionary<string, ZdValue> { ["name"] = new ZdValue.String("火药"), ["n"] = new ZdValue.Integer(1) }),
            new ZdValue.Map(new Dictionary<string, ZdValue> { ["name"] = new ZdValue.String("火药"), ["n"] = new ZdValue.Integer(2) }),
        });

        var pool = new ZdStringPool();
        byte[] enc = ZdCodec.EncodeWithPool(rows, pool);
        // 池段 = 数组头 + 唯一字符串： {name, 火药, n}
        Assert.Equal(3, pool.Count);
        Assert.Contains("name", pool.UniqueStrings);
        Assert.Contains("火药", pool.UniqueStrings);
        Assert.Contains("n", pool.UniqueStrings);
        // 正文中出现 0xd8 引用（至少 4 个 map 键全部走引用）
        int refs = enc.Count(b => b == 0xD8);
        Assert.True(refs >= 4, $"应有多个 0xd8 引用，实际 {refs}");

        var decPool = new ZdStringPool();
        ZdValue back = ZdCodec.DecodeWithPool(enc, decPool);
        Assert.Equal(rows.ToString(), back.ToString());
        Assert.True(rows.DeepEquals(back));
    }

    [Fact]
    public void StringDict_Flag_Roundtrip()
    {
        var m = new ZdValue.Map(new Dictionary<string, ZdValue> { ["a"] = new ZdValue.String("重复"), ["b"] = new ZdValue.String("重复") });
        byte[] enc = ZdCodec.EncodeWithPool(m);
        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"zd_dict_{System.Guid.NewGuid():N}.zd");
        try
        {
            Assert.True(Zd.SaveWithFlags(tmp, Zd.FlagDict, enc));
            byte[] raw = System.IO.File.ReadAllBytes(tmp);
            Assert.Equal(ZdVersion.V2, Zd.DetectVersion(raw));
            Assert.True((Zd.GetFlags(raw) & Zd.FlagDict) != 0);
            ZdValue back = ZdCodec.DecodeWithPool(Zd.Load(tmp));
            Assert.True(m.DeepEquals(back));
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    // ==================== 列式容器 ====================

    [Fact]
    public void Columnar_Roundtrip()
    {
        var col = new ZdValue.Columnar(new[]
        {
            new Column("name", "string",
                new ZdValue[] { new ZdValue.String("刺客"), new ZdValue.String("战士"), new ZdValue.String("法师") }),
            new Column("power", "int",
                new ZdValue[] { new ZdValue.Integer(9), new ZdValue.Integer(7), new ZdValue.Integer(8) }),
            new Column("active", "bool",
                new ZdValue[] { new ZdValue.Bool(true), new ZdValue.Bool(false), new ZdValue.Bool(true) }),
        });

        byte[] enc = ZdCodec.Encode(col);
        Assert.Equal(0xD6, enc[0]);                     // 0xD6 列式标签
        ZdValue back = ZdCodec.Decode(enc);
        Assert.True(col.DeepEquals(back));
        Assert.IsType<ZdValue.Columnar>(back);

        // 列式位 flag 落地
        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"zd_col_{System.Guid.NewGuid():N}.zd");
        try
        {
            Assert.True(Zd.SaveWithFlags(tmp, Zd.FlagColumnar, enc));
            Assert.True((Zd.GetFlags(System.IO.File.ReadAllBytes(tmp)) & Zd.FlagColumnar) != 0);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    // ==================== Schema 段 + 内容哈希段 ====================

    [Fact]
    public void Container_Schema_And_Hash()
    {
        var v = new ZdValue.Map(new Dictionary<string, ZdValue>
        {
            ["name"] = new ZdValue.String("火药"),
            ["power"] = new ZdValue.Integer(9),
        });
        var opts = new ZdV2Options
        {
            IncludeSchema = true,
            Schema = new List<KeyValuePair<string, string>>
            {
                new("name", "string"),
                new("power", "int"),
            },
            IncludeHash = true,
            HashAlgo = ZdHashAlgo.Crc32,
        };

        byte[] container = ZdV2.Encode(v, opts);
        Assert.Equal(ZdVersion.V2, Zd.DetectVersion(container));
        byte flags = Zd.GetFlags(container);
        Assert.True((flags & Zd.FlagSchema) != 0);
        Assert.True((flags & Zd.FlagHash) != 0);
        Assert.True((flags & Zd.FlagColumnar) == 0);

        var res = new ZdV2Result();
        ZdValue back = ZdV2.Decode(container, res);
        Assert.True(v.DeepEquals(back));
        Assert.NotNull(res.Schema);
        Assert.Equal(2, res.Schema!.Count);
        Assert.Equal("name", res.Schema[0].Key);
        Assert.Equal("string", res.Schema[0].Value);
        Assert.Equal(ZdHashAlgo.Crc32, res.HashAlgo);
        Assert.True(res.HashVerified);
        Assert.Null(res.HashError);

        // 篡改正文 → 哈希校验失败
        byte[] tampered = (byte[])container.Clone();
        int lastIndex = tampered.Length - 1;
        tampered[lastIndex] ^= 0xFF;
        var res2 = new ZdV2Result();
        ZdV2.Decode(tampered, res2);
        Assert.False(res2.HashVerified);
        Assert.NotNull(res2.HashError);
    }

    [Fact]
    public void Container_Sha256_Hash()
    {
        var v = new ZdValue.String("内容完整性测试");
        var opts = new ZdV2Options { IncludeHash = true, HashAlgo = ZdHashAlgo.Sha256 };
        byte[] container = ZdV2.Encode(v, opts);
        var res = new ZdV2Result();
        Assert.True(v.DeepEquals(ZdV2.Decode(container, res)));
        Assert.Equal(ZdHashAlgo.Sha256, res.HashAlgo);
        Assert.True(res.HashVerified);
    }
}