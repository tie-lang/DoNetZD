using DoNetZD;
using Xunit;

namespace DoNetZD.Tests;

public enum ZdColor { Red, Green, Blue }

public class ZdInner
{
    public int N;
    public string Label = "";
}

public class ZdSample
{
    public int Id { get; set; }
    [ZdName("display_name")]
    public string Name { get; set; } = "";
    [ZdIgnore]
    public string Secret { get; set; } = "";
    public ZdColor Color { get; set; }
    public ZdInner? Meta { get; set; }
    public List<int> Tags { get; set; } = new();
    public List<ZdInner> Inners { get; set; } = new();
    public Dictionary<string, int> ScoreMap { get; set; } = new();
    public int[] Arr { get; set; } = Array.Empty<int>();
}

/// <summary>POCO 反射绑定：序列化 / 反序列化 / 特性 / 嵌套 / 容器。</summary>
public class SerializerTests
{
    private static ZdSample NewSample() => new()
    {
        Id = 42,
        Name = "照片",
        Secret = "hidden",
        Color = ZdColor.Blue,
        Meta = new ZdInner { N = 7, Label = "meta-label" },
        Tags = { 1, 2, 3 },
        Inners = { new ZdInner { N = 10, Label = "a" }, new ZdInner { N = 20, Label = "b" } },
        ScoreMap = { ["x"] = 100, ["y"] = 200 },
        Arr = new[] { 9, 8, 7 },
    };

    [Fact]
    public void FromObject_Poco_RespectsAttributes()
    {
        ZdValue v = ZdValue.FromObject(NewSample());
        var m = Assert.IsType<ZdValue.Map>(v);
        Assert.True(m.Entries.ContainsKey("display_name"));
        Assert.False(m.Entries.ContainsKey("Name"));      // 被 ZdName 改名
        Assert.False(m.Entries.ContainsKey("Secret"));     // 被 ZdIgnore 忽略
        Assert.True(m.Entries.ContainsKey("Color"));
        Assert.Equal(42L, ((ZdValue.Integer)m.Entries["Id"]).Value);
        Assert.Equal("照片", ((ZdValue.String)m.Entries["display_name"]).Value);
        // 枚举 → Integer（Blue=2）
        Assert.Equal(2L, ((ZdValue.Integer)m.Entries["Color"]).Value);
    }

    [Fact]
    public void SerializeDeserialize_Roundtrip()
    {
        ZdSample src = NewSample();
        byte[] bytes = ZdSerializer.Serialize(src);
        ZdSample back = ZdSerializer.Deserialize<ZdSample>(bytes);

        Assert.Equal(src.Id, back.Id);
        Assert.Equal(src.Name, back.Name);
        Assert.Equal("", back.Secret);                    // 忽略字段不回填
        Assert.Equal(src.Color, back.Color);
        Assert.NotNull(back.Meta);
        Assert.Equal(7, back.Meta!.N);
        Assert.Equal("meta-label", back.Meta.Label);
        Assert.Equal(new[] { 1, 2, 3 }, back.Tags);
        Assert.Equal(2, back.Inners.Count);
        Assert.Equal(10, back.Inners[0].N);
        Assert.Equal("b", back.Inners[1].Label);
        Assert.Equal(100, back.ScoreMap["x"]);
        Assert.Equal(200, back.ScoreMap["y"]);
        Assert.Equal(new[] { 9, 8, 7 }, back.Arr);
    }

    [Fact]
    public void ToObject_Primitives()
    {
        Assert.Equal(42, new ZdValue.Integer(42).ToObject<int>());
        Assert.Equal(3.14, new ZdValue.Float(3.14).ToObject<double>());
        Assert.True(new ZdValue.Bool(true).ToObject<bool>());
        Assert.Equal("hi", new ZdValue.String("hi").ToObject<string>());
        Assert.Equal(ZdColor.Green, new ZdValue.Integer(1).ToObject<ZdColor>());
    }

    [Fact]
    public void ToObject_Nullable()
    {
        Assert.Null(ZdValue.Null.Instance.ToObject<int?>());
        Assert.Equal(7, new ZdValue.Integer(7).ToObject<int?>());
    }
}
