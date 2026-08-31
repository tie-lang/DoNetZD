using DoNetZD;
using Xunit;

namespace DoNetZD.Tests;

/// <summary>
/// v1 兼容：v2 实现必须能读 v1（8 字节头 "TIEDBZD" + 0x01）文件与字节流。
/// v1 从不落盘 0xc6（v1 中 0xc6 为 tuple，未实现），因此 v2 把 0xc6 复用作 bytes 不冲突。
/// </summary>
public class V1CompatTests
{
    [Fact]
    public void V1_Header_Detected()
    {
        byte[] v1File = Zd.Concat(Zd.V1Header, new byte[] { 0x05 });   // 头 + fixint 5
        Assert.Equal(ZdVersion.V1, Zd.DetectVersion(v1File));
        Assert.True(Zd.IsZd(v1File));
        Assert.False(Zd.IsV2(v1File));
        Assert.Equal(0, Zd.GetFlags(v1File));

        Assert.True(Zd.V1Header.Length == 8);
        Assert.Equal([0x54, 0x49, 0x45, 0x44, 0x42, 0x5A, 0x44, 0x01], Zd.V1Header);
    }

    [Fact]
    public void V1_Body_Loads_And_Decodes()
    {
        // 手工构造 v1 文件：8 字节旧头 + 正文（encode_i64 → fixint，随之字符串）
        var body = new ZdBuilder();
        body.AppendBytes(Zd.EncodeI64(1234));
        body.AppendBytes(Zd.EncodeString("v1 中文"));
        byte[] v1File = Zd.Concat(Zd.V1Header, body.ToArray());

        string tmp = Path.Combine(Path.GetTempPath(), $"zd_v1_{Guid.NewGuid():N}.zd");
        try
        {
            File.WriteAllBytes(tmp, v1File);
            byte[] loaded = Zd.Load(tmp);     // v1 头被识别并去头
            Assert.Equal(Hex(body.ToArray()), Hex(loaded));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void V1_Integer_Body_NonTrivial_NotMistakenAsV2()
    {
        // 关键回归：v1 文件正文首字节若 < 48（会被误判为 v2 版本位），也必须识别为 v1
        byte[] v1File = Zd.Concat(Zd.V1Header, new byte[] { 0x2A });   // 0x2A = fixint 42，<48
        Assert.Equal(ZdVersion.V1, Zd.DetectVersion(v1File));
        Assert.True(Zd.IsZd(v1File));
        // 去头返回正文（v1 头 8 字节）
        Assert.Equal([0x2A], v1File.Skip(8).ToArray());
    }

    [Fact]
    public void V1_Stream_Save_Still_Writes_V2()
    {
        // Save 默认写 v2 头（10 字节）；但读回时对 v1 头文件兼容
        string tmp = Path.Combine(Path.GetTempPath(), $"zd_round_{Guid.NewGuid():N}.zd");
        try
        {
            byte[] body = Zd.EncodeI64(7);
            Assert.True(Zd.Save(tmp, body));
            byte[] onDisk = File.ReadAllBytes(tmp);
            Assert.Equal(ZdVersion.V2, Zd.DetectVersion(onDisk));
            Assert.Equal(10, onDisk.Length - body.Length);
            Assert.Equal(Hex(body), Hex(Zd.Load(tmp)));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static string Hex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));
}