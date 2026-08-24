using System.Text;
using DoNetZD;
using Xunit;

namespace DoNetZD.Tests;

/// <summary>CRC32 已知向量。</summary>
public class CrcTests
{
    [Fact]
    public void Compute_KnownVector()
    {
        // CRC32("123456789") 标准结果 0xCBF43926
        byte[] data = Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0xCBF43926u, ZdCrc32.Compute(data));
    }

    [Fact]
    public void Compute_Empty_IsZero()
    {
        Assert.Equal(0u, ZdCrc32.Compute(Array.Empty<byte>()));
        Assert.Equal(0u, ZdCrc32.Compute(null!));
    }

    [Fact]
    public void ComputeBe_IsBigEndian()
    {
        byte[] data = Encoding.ASCII.GetBytes("123456789");
        byte[] be = ZdCrc32.ComputeBe(data);
        Assert.Equal(new byte[] { 0xCB, 0xF4, 0x39, 0x26 }, be);
    }

    [Fact]
    public void Compute_DifferentInputDifferentCrc()
    {
        Assert.NotEqual(ZdCrc32.Compute(Encoding.ASCII.GetBytes("a")), ZdCrc32.Compute(Encoding.ASCII.GetBytes("b")));
    }
}
