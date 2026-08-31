using DoNetZD;
using Xunit;

namespace DoNetZD.Tests;

/// <summary>JSON ↔ zd。</summary>
public class JsonTests
{
    private static string RoundTrip(string json)
    {
        ZdValue v = JsonCodec.Parse(json);
        return JsonCodec.Serialize(v);
    }

    [Fact]
    public void Parse_BasicKinds()
    {
        Assert.Equal(42, ((ZdValue.Integer)JsonCodec.Parse("42")).Value);
        Assert.Equal(2.5, ((ZdValue.Float)JsonCodec.Parse("2.5")).Value);
        Assert.Equal(-3, ((ZdValue.Integer)JsonCodec.Parse("-3")).Value);
        Assert.True(((ZdValue.Bool)JsonCodec.Parse("true")).Value);
        Assert.False(((ZdValue.Bool)JsonCodec.Parse("false")).Value);
        Assert.Equal("hi", ((ZdValue.String)JsonCodec.Parse("\"hi\"")).Value);
    }

    [Fact]
    public void Parse_NullSentinel()
    {
        Assert.Same(ZdValue.Null.Instance, JsonCodec.Parse("null"));
    }

    [Fact]
    public void RoundTrip_Simple()
    {
        Assert.Equal("""{"a":1,"b":[true,[1.5,"x"]],"c":null}""",
            RoundTrip("""{"a":1,"b":[true,[1.5,"x"]],"c":null}"""));
    }

    [Fact]
    public void StringEscape_RoundTrip()
    {
        Assert.Equal("""{"s":"a\"b\\\n中\t"}""", RoundTrip("""{"s":"a\"b\\\n中\t"}"""));
    }

    [Fact]
    public void ToBytes_WithNull_IsEncodable()
    {
        // v2 起 null 为核心类型（0xc0）：Json 的 null → Null 值可编码且回环
        ZdValue v = JsonCodec.Parse("{\"a\":null}");
        byte[] enc = ZdCodec.Encode(v);
        var back = (ZdValue.Map)ZdCodec.Decode(enc);
        Assert.IsType<ZdValue.Null>(back.Entries["a"]);
        Assert.Equal([0xC0], Zd.EncodeNull());
    }

    [Fact]
    public void InvalidJson_Throws()
    {
        Assert.Throws<FormatException>(() => JsonCodec.Parse("{\"a\":}"));
        Assert.Throws<FormatException>(() => JsonCodec.Parse("[1,2"));
    }
}

/// <summary>字节 / base64 ↔ zd。</summary>
public class BytesTests
{
    [Fact]
    public void Bytes_RoundTrip()
    {
        byte[] src = [0x00, 0x7F, 0x80, 0xFF, 0x10, 0x20];
        ZdValue v = BytesCodec.FromBytes(src);
        Assert.Equal(src, BytesCodec.ToBytes(v));
    }

    [Fact]
    public void Base64_RoundTrip()
    {
        byte[] src = System.Text.Encoding.UTF8.GetBytes("DoNetZD → tie:zd");
        string b64 = Convert.ToBase64String(src);
        ZdValue v = BytesCodec.FromBase64(b64);
        Assert.Equal(src, BytesCodec.ToBytes(v));
        Assert.Equal(b64, HasToBase64(v));
    }

    private static string HasToBase64(ZdValue v) => BytesCodec.ToBase64(v);
}

/// <summary>CSV ↔ zd。</summary>
public class CsvTests
{
    [Fact]
    public void FromCsv_Simple()
    {
        var v = CsvCodec.FromCsv("name,age\nalice,30\nbob,25\n") as ZdValue.Array;
        Assert.NotNull(v);
        Assert.Equal(3, v.Items.Count); // 表头 + 2 数据行
        Assert.Equal("alice", ((ZdValue.String)((ZdValue.Array)v.Items[1]).Items[0]).Value);
        Assert.Equal("30", ((ZdValue.String)((ZdValue.Array)v.Items[1]).Items[1]).Value);
    }

    [Fact]
    public void ToCsv_RoundTrip_Semantic()
    {
        ZdValue a = CsvCodec.FromCsv("a,\"b,c\",d\n1,2,3\n");
        ZdValue b = CsvCodec.FromCsv(CsvCodec.ToCsv(a));
        Assert.Equal(((ZdValue.Array)a).Items.Count, ((ZdValue.Array)b).Items.Count);
    }

    [Fact]
    public void QuotedField_Parsed()
    {
        var v = CsvCodec.FromCsv("x,\"hello, world\",y") as ZdValue.Array;
        var row = (ZdValue.Array)v.Items[0];
        Assert.Equal("hello, world", ((ZdValue.String)row.Items[1]).Value);
    }
}

/// <summary>INI ↔ zd。</summary>
public class IniTests
{
    [Fact]
    public void FromIni_SectionsAndGlobal()
    {
        string ini = "g=1\n[sec]\nk=v\nanswer=42\n";
        var v = CsvOrIni(ini) as ZdValue.Map;
        Assert.NotNull(v);
        var g = (ZdValue.Map)v.Entries[""];
        var sec = (ZdValue.Map)v.Entries["sec"];
        Assert.Equal("1", ((ZdValue.String)g.Entries["g"]).Value);
        Assert.Equal("v", ((ZdValue.String)sec.Entries["k"]).Value);
        Assert.Equal("42", ((ZdValue.String)sec.Entries["answer"]).Value);
    }

    [Fact]
    public void ToIni_RoundTrip_Semantic()
    {
        ZdValue a = IniCodec.FromIni("[s]\nk=v\n[g]\na=1\n");
        ZdValue b = IniCodec.FromIni(IniCodec.ToIni(a));
        Assert.Equal(ValueText(a), ValueText(b));
    }

    private static ZdValue CsvOrIni(string s) => IniCodec.FromIni(s);
    private static string ValueText(ZdValue v) => v.ToString();
}

/// <summary>XML ↔ zd。</summary>
public class XmlTests
{
    private const string Xml = "<person name=\"阿\"><age>30</age><tag>a</tag><tag>b</tag></person>";

    [Fact]
    public void FromXml_MapsAttrsAndChildren()
    {
        var v = XmlCodec.FromXml(Xml) as ZdValue.Map;
        Assert.NotNull(v);
        Assert.Equal("阿", ((ZdValue.String)v.Entries["@name"]).Value);
        Assert.Equal("30", ((ZdValue.String)v.Entries["age"]).Value);
        var tags = (ZdValue.Array)v.Entries["tag"];
        Assert.Equal(2, tags.Items.Count);
    }

    [Fact]
    public void ToXml_ThenFromXml_RoundTrip()
    {
        ZdValue a = XmlCodec.FromXml(Xml);
        string xml = XmlCodec.ToXml(a, "person");
        ZdValue b = XmlCodec.FromXml(xml);
        Assert.Equal(a.ToString(), b.ToString());
    }
}

/// <summary>zd 字节 ↔ 各格式（经 ZdConvert 门面）。</summary>
public class ConvertFacadeTests
{
    [Fact]
    public void Json_ToBytes_BackToJson()
    {
        byte[] bytes = ZdConvert.JsonToBytes("""{"n":1,"s":"hi","ok":true}""");
        string json = ZdConvert.BytesToJson(bytes);
        Assert.Contains("\"n\":1", json);
        Assert.Contains("\"ok\":true", json);
    }

    [Fact]
    public void TieData_AliasJson()
    {
        ZdValue v = ZdConvert.TieDataToValue("{\"k\":[1,2,3]}");
        Assert.IsType<ZdValue.Map>(v);
    }

    [Fact]
    public void BytesJson_AllFormatsRound()
    {
        byte[] bytes = ZdConvert.JsonToBytes("""{"id":7}""");
        ZdValue v = ZdCodec.Decode(bytes);
        Assert.Equal(7, ((ZdValue.Integer)((ZdValue.Map)v).Entries["id"]).Value);
    }
}