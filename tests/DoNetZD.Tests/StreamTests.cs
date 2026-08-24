using System.IO;
using DoNetZD;
using Xunit;

namespace DoNetZD.Tests;

/// <summary>流式编解码 + 异步文件 IO + CRC 校验文件。</summary>
public class StreamTests
{
    private static ZdValue Map(params (string k, ZdValue v)[] pairs)
    {
        var d = new Dictionary<string, ZdValue>();
        foreach (var (k, v) in pairs) d[k] = v;
        return new ZdValue.Map(d);
    }

    [Fact]
    public void Encode_Direct_Equals_ZdCodec()
    {
        var root = Map(
            ("width", new ZdValue.Integer(1920)),
            ("name", new ZdValue.String("照片")),
            ("list", new ZdValue.Array(new ZdValue[]
            {
                new ZdValue.Integer(1),
                new ZdValue.Float(2.5),
                new ZdValue.Bool(true),
            })));

        byte[] viaStream;
        using (var ms = new MemoryStream())
        {
            ZdStream.Encode(ms, root);
            viaStream = ms.ToArray();
        }

        byte[] viaCodec = ZdCodec.Encode(root);
        Assert.Equal(Hex(viaCodec), Hex(viaStream));

        ZdValue back = ZdStream.Decode(new MemoryStream(viaStream));
        Assert.True(root.DeepEquals(back));
    }

    [Fact]
    public void Decode_StreamRoundtrip_Nested()
    {
        var root = Map(("deep", Map(("a", new ZdValue.Integer(7)), ("b", new ZdValue.String("中")))));
        using var ms = new MemoryStream();
        ZdStream.Encode(ms, root);
        ms.Position = 0;
        ZdValue back = ZdStream.Decode(ms);
        Assert.True(root.DeepEquals(back));
    }

    [Fact]
    public async Task SaveLoadAsync_Roundtrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"zd_async_{Guid.NewGuid():N}.zd");
        try
        {
            byte[] body = ZdCodec.Encode(Map(("v", new ZdValue.Integer(99))));
            Assert.True(await Zd.SaveAsync(path, body));
            byte[] loaded = await Zd.LoadAsync(path);
            Assert.Equal(Hex(body), Hex(loaded));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveLoadChecked_Roundtrip_AndTamperDetection()
    {
        string path = Path.Combine(Path.GetTempPath(), $"zd_crc_{Guid.NewGuid():N}.zd");
        try
        {
            byte[] body = ZdCodec.Encode(Map(("v", new ZdValue.Integer(12345))));
            Assert.True(Zd.SaveChecked(path, body));

            // 正常读回
            byte[] loaded = Zd.LoadChecked(path);
            Assert.Equal(Hex(body), Hex(loaded));

            // 篡改 body 中间一字节 → CRC 失败
            byte[] raw = File.ReadAllBytes(path);
            raw[raw.Length - 1] ^= 0xFF;
            File.WriteAllBytes(path, raw);
            var ex = Assert.Throws<InvalidDataException>(() => Zd.LoadChecked(path));
            Assert.Contains("CRC 校验失败", ex.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string Hex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));
}
