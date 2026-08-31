using System.Diagnostics;
using DoNetZD;
using Xunit;

namespace DoNetZD.Tests;

/// <summary>字节构建器（append-only buffer）正确性与规模性能探针。</summary>
public class BuilderTests
{
    [Fact]
    public void Builder_Basic()
    {
        var b = new ZdBuilder();
        Assert.Equal(0, b.Length);
        b.AppendByte(0x01).AppendByte(0x02).AppendByte(0x03);
        Assert.Equal(3, b.Length);
        Assert.Equal([0x01, 0x02, 0x03], b.ToArray());
    }

    [Fact]
    public void Builder_AppendBytes_And_Reserve()
    {
        var b = new ZdBuilder(4);
        b.AppendBytes([0x0A, 0x0B, 0x0C, 0x0D]);
        Assert.Equal(4, b.Length);
        b.Reserve(100);                        // 预分配：容量应 ≥ 104
        Assert.True(b.Capacity >= 104);
        b.AppendBytes([0xEE, 0xFF]);
        Assert.Equal(6, b.Length);
        Assert.Equal([0x0A, 0x0B, 0x0C, 0x0D, 0xEE, 0xFF], b.ToArray());
    }

    [Fact]
    public void Builder_Chunks_Match_Concat()
    {
        // 分段写入应等于连续拼接
        var b = new ZdBuilder();
        for (int i = 0; i < 100; i++)
            b.AppendByte((byte)i);
        byte[] chunked = b.ToArray();

        byte[] concat = new byte[100];
        for (int i = 0; i < 100; i++) concat[i] = (byte)i;
        Assert.Equal(Hex(concat), Hex(chunked));
    }

    [Fact]
    public void Codec_LargeArray_Roundtrip_Correct()
    {
        // 大规模数组经 builder 编码后回环与原文一致（正确性护栏）
        var items = new ZdValue[20000];
        for (int i = 0; i < items.Length; i++)
            items[i] = new ZdValue.Integer(i);

        byte[] enc = ZdCodec.Encode(new ZdValue.Array(items));
        var back = (ZdValue.Array)ZdCodec.Decode(enc);
        Assert.Equal(items.Length, back.Items.Count);
        for (int i = 0; i < 20000; i++)
            Assert.Equal(i, ((ZdValue.Integer)back.Items[i]).Value);
    }

    [Fact]
    public void Codec_LargeArray_EncodeFast()
    {
        // 性能探针：2 万元素数组编码应在宽松上限内完成（非 O(n²)）
        var items = new ZdValue[20000];
        for (int i = 0; i < items.Length; i++)
            items[i] = new ZdValue.Integer(i);
        var sw = Stopwatch.StartNew();
        byte[] enc = ZdCodec.Encode(new ZdValue.Array(items));
        sw.Stop();
        Assert.True(enc.Length > 0);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"编码耗时过长：{sw.ElapsedMilliseconds}ms");
    }

    private static string Hex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));
}