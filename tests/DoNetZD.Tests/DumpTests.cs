using DoNetZD;
using Xunit;

namespace DoNetZD.Tests;

/// <summary>字节可视化转储。</summary>
public class DumpTests
{
    [Fact]
    public void Dump_IncludesTypeAnnotations()
    {
        var root = new ZdValue.Map(new Dictionary<string, ZdValue>
        {
            ["port"] = new ZdValue.Integer(8080),
            ["host"] = new ZdValue.String("0.0.0.0"),
            ["ok"] = new ZdValue.Bool(true),
        });
        string dump = ZdDump.Dump(ZdCodec.Encode(root));

        Assert.Contains("map(3)", dump);
        Assert.Contains("key \"port\"", dump);
        Assert.Contains("u16 8080", dump);            // 8080 → u16
        Assert.Contains("key \"host\"", dump);
        Assert.Contains("bool true", dump);
    }

    [Fact]
    public void Dump_WithMagicHeader()
    {
        byte[] body = ZdCodec.Encode(new ZdValue.Integer(42));
        byte[] file = Zd.Concat(Zd.MagicHeader, body);
        string dump = ZdDump.Dump(file);
        Assert.Contains("TIEDBZD v1", dump);
        Assert.Contains("fixint+ 42", dump);
    }

    [Fact]
    public void Dump_TrailingBytesFlagged()
    {
        byte[] body = ZdCodec.Encode(new ZdValue.Integer(1));
        byte[] withTrailer = Zd.Concat(body, new byte[] { 0xFF, 0xFF });
        string dump = ZdDump.Dump(withTrailer);
        Assert.Contains("尾随", dump);
    }

    [Fact]
    public void ZdConvert_Dump_Facade()
    {
        var v = new ZdValue.Array(new ZdValue[] { new ZdValue.Integer(1), new ZdValue.Integer(2) });
        string dump = ZdConvert.Dump(ZdCodec.Encode(v));
        Assert.Contains("array(2)", dump);
    }
}
