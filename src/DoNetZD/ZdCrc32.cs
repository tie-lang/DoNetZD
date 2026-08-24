namespace DoNetZD;

/// <summary>
/// CRC32（IEEE 802.3，多项式 0xEDB88320）自实现，零依赖。
/// 用于 zd 文件可选完整性校验（见 <see cref="Zd.SaveChecked"/>/<see cref="Zd.LoadChecked"/>），
/// 也可独立用于任意字节序列的校验和。
/// </summary>
public static class ZdCrc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
            t[i] = c;
        }
        return t;
    }

    /// <summary>计算字节序列的 CRC32（大端多项式，初值 0xFFFFFFFF，结果取反）。</summary>
    public static uint Compute(byte[] data)
    {
        if (data is null || data.Length == 0)
            return 0;
        uint c = 0xFFFFFFFFu;
        for (int i = 0; i < data.Length; i++)
            c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    /// <summary>计算 CRC32 并以 4 字节大端返回（便于直接写入文件头）。</summary>
    public static byte[] ComputeBe(byte[] data) => Zd.WriteU32Be(Compute(data));
}
