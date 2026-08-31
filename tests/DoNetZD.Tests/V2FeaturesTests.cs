using DoNetZD;
using Xunit;

namespace DoNetZD.Tests;

/// <summary>zd v2 新增能力：字符串字典/引用、列式、schema、内容哈希（golden 向量 + 回环）。</summary>
public class V2FeaturesTests
{
    private static string Hex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));

    private static void AssertBytes(byte[] expected, byte[] actual) => Assert.Equal(Hex(expected), Hex(actual));

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
}