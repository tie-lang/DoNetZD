using DoNetZD;
using Xunit;

namespace DoNetZD.Tests;

/// <summary>TOML ↔ zd。</summary>
public class TomlTests
{
    [Fact]
    public void FromToml_Basic()
    {
        var v = TomlCodec.FromToml("title = \"tie\"\ncount = 3\nratio = 1.5\n on=true\n") as ZdValue.Map;
        Assert.NotNull(v);
        Assert.Equal("tie", ((ZdValue.String)v.Entries["title"]).Value);
        Assert.Equal(3L, ((ZdValue.Integer)v.Entries["count"]).Value);
        Assert.Equal(1.5, ((ZdValue.Float)v.Entries["ratio"]).Value);
        Assert.True(((ZdValue.Bool)v.Entries["on"]).Value);
    }

    [Fact]
    public void FromToml_TableAndArrayTable()
    {
        string toml = "[server]\nhost=\"h\"\nport=80\n\n[[items]]\nname=\"a\"\nvalue=1\n\n[[items]]\nname=\"b\"\nvalue=2\n";
        var v = TomlCodec.FromToml(toml) as ZdValue.Map;
        var server = (ZdValue.Map)v.Entries["server"];
        Assert.Equal("h", ((ZdValue.String)server.Entries["host"]).Value);
        var items = (ZdValue.Array)v.Entries["items"];
        Assert.Equal(2, items.Items.Count);
        Assert.Equal("b", ((ZdValue.String)((ZdValue.Map)items.Items[1]).Entries["name"]).Value);
    }

    [Fact]
    public void FromToml_DottedAndInline()
    {
        var v = TomlCodec.FromToml("a.b.c=7\narr=[1,\"two\",3.0]\nin={x=1,y=\"z\"}\n") as ZdValue.Map;
        var a = (ZdValue.Map)v.Entries["a"];
        var b = (ZdValue.Map)a.Entries["b"];
        Assert.Equal(7L, ((ZdValue.Integer)b.Entries["c"]).Value);
        var arr = (ZdValue.Array)v.Entries["arr"];
        Assert.Equal(3, arr.Items.Count);
        var inl = (ZdValue.Map)v.Entries["in"];
        Assert.Equal("z", ((ZdValue.String)inl.Entries["y"]).Value);
    }

    [Fact]
    public void ToToml_RoundTrip_Semantic()
    {
        ZdValue a = TomlCodec.FromToml("[s]\nk=v\nn=3\n[[t]]\nx=1\n[[t]]\nx=2\n");
        ZdValue b = TomlCodec.FromToml(TomlCodec.ToToml(a));
        Assert.Equal(a.ToString(), b.ToString());
    }
}

/// <summary>YAML ↔ zd。</summary>
public class YamlTests
{
    [Fact]
    public void FromYaml_MappingAndNested()
    {
        string yaml = "name: 张三\nage: 30\naddress:\n  city: 北京\n  zip: '100000'\nflags:\n  - a\n  - b\n  - 3\n";
        var v = YamlCodec.FromYaml(yaml) as ZdValue.Map;
        Assert.NotNull(v);
        Assert.Equal("张三", ((ZdValue.String)v.Entries["name"]).Value);
        Assert.Equal(30L, ((ZdValue.Integer)v.Entries["age"]).Value);
        var addr = (ZdValue.Map)v.Entries["address"];
        Assert.Equal("北京", ((ZdValue.String)addr.Entries["city"]).Value);
        Assert.Equal("100000", ((ZdValue.String)addr.Entries["zip"]).Value);
        Assert.True(v.Entries["flags"] is ZdValue.Array, JsonCodec.Serialize(v));
        var flags = (ZdValue.Array)v.Entries["flags"];
        Assert.Equal(3, flags.Items.Count);
    }

    [Fact]
    public void FromYaml_SequenceOfMappings()
    {
        string yaml = "- name: a\n  v: 1\n- name: b\n  v: 2\n";
        var v = YamlCodec.FromYaml(yaml) as ZdValue.Array;
        Assert.NotNull(v);
        Assert.Equal(2, v.Items.Count);
        Assert.Equal("b", ((ZdValue.String)((ZdValue.Map)v.Items[1]).Entries["name"]).Value);
    }

    [Fact]
    public void FromYaml_FlowAndScalars()
    {
        string yaml = "list: [1, 2, 3]\nmap: {x: 1, y: hi}\nempty: null\nflag: true\n";
        var v = YamlCodec.FromYaml(yaml) as ZdValue.Map;
        var list = (ZdValue.Array)v.Entries["list"];
        Assert.Equal(3, list.Items.Count);
        var map = (ZdValue.Map)v.Entries["map"];
        Assert.Equal("hi", ((ZdValue.String)map.Entries["y"]).Value);
        Assert.Same(ZdValue.Null.Instance, v.Entries["empty"]);
        Assert.True(((ZdValue.Bool)v.Entries["flag"]).Value);
    }

    [Fact]
    public void ToYaml_RoundTrip_Semantic()
    {
        ZdValue a = YamlCodec.FromYaml("a: 1\nb:\n  c: hey\n  d: [x, y]\n");
        ZdValue b = YamlCodec.FromYaml(YamlCodec.ToYaml(a));
        Assert.Equal(a.ToString(), b.ToString());
    }

    [Fact]
    public void BlockScalar_Literal()
    {
        var v = YamlCodec.FromYaml("text: |\n  line1\n  line2\n") as ZdValue.Map;
        string s = ((ZdValue.String)v.Entries["text"]).Value;
        Assert.Contains("line1", s);
        Assert.Contains("line2", s);
    }
}

/// <summary>YAML / TOML 经 ZdConvert 门面 & 跨格式。</summary>
public class YamlTomlFacadeTests
{
    [Fact]
    public void Facade_RoundTrip()
    {
        ZdValue yaml = ZdConvert.YamlToValue("name: x\nlist:\n  - 1\n  - 2\n");
        string toml = ZdConvert.ValueToToml(yaml);
        ZdValue back = ZdConvert.TomlToValue(toml);
        Assert.IsType<ZdValue.Map>(back);
    }

    [Fact]
    public void CrossFormat_PreserveScalars()
    {
        // JSON → YAML → zd 值；整数值保持
        ZdValue j = JsonCodec.Parse("{\"n\":5,\"s\":\"hi\"}");
        string yaml = ZdConvert.ValueToYaml(j);
        ZdValue y = YamlCodec.FromYaml(yaml);
        Assert.Equal(5L, ((ZdValue.Integer)((ZdValue.Map)y).Entries["n"]).Value);
        Assert.Equal("hi", ((ZdValue.String)((ZdValue.Map)y).Entries["s"]).Value);
    }
}