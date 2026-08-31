using System.Security.Cryptography;

namespace DoNetZD;

/// <summary>v2 内容哈希算法（tsha1x/fnv 的占位，实际用 CRC32 或 SHA256）。</summary>
public enum ZdHashAlgo
{
    /// <summary>CRC32（4 字节，快速、防意外篡改）。</summary>
    Crc32 = 0,
    /// <summary>SHA-256（32 字节，强抗碰撞）。</summary>
    Sha256 = 1,
}

/// <summary>
/// v2 内容哈希段：容器内嵌正文摘要，读端完整性校验。
/// 字节格式：<c>0xc1 + algo(fixint 0/1) + 0xc6+数组头+哈希字节</c>。
/// </summary>
public static class ZdContentHash
{
    /// <summary>对正文计算指定算法哈希。</summary>
    public static byte[] Compute(byte[] data, ZdHashAlgo algo)
    {
        switch (algo)
        {
            case ZdHashAlgo.Crc32:
                return Zd.WriteU32Be(ZdCrc32.Compute(data));
            case ZdHashAlgo.Sha256:
                using (var sha = SHA256.Create())
                    return sha.ComputeHash(data);
            default:
                throw new ArgumentOutOfRangeException(nameof(algo));
        }
    }

    /// <summary>构造内容哈希段。</summary>
    public static byte[] Encode(byte[] content, ZdHashAlgo algo)
    {
        byte[] h = Compute(content, algo);
        var b = new ZdBuilder(h.Length + 12);
        b.AppendByte(Zd.TagHash);
        b.AppendByte((byte)(int)algo);                 // 0/1 → 单字节 fixint
        b.AppendBytes(Zd.EncodeBytes(h));
        return b.ToArray();
    }

    /// <summary>解析内容哈希段（pos 指向 0xc1，消费整段），返回算法与哈希字节。</summary>
    public static void Decode(byte[] t, ref int pos, out ZdHashAlgo algo, out byte[] hash)
    {
        int start = pos;
        if (t is null || pos >= t.Length || t[pos] != Zd.TagHash)
            throw new ZdFormatException($"期望内容哈希段(0xc1)，实际 0x{(t is null || pos >= t.Length ? "EOF" : t[pos].ToString("X2"))}", start);
        pos++;
        if (pos >= t.Length)
            throw new ZdFormatException("内容哈希段缺算法字节", start);
        algo = (ZdHashAlgo)t[pos++];
        hash = Zd.DecodeBytes(t, ref pos);
    }

    /// <summary>校验：对 content 重算哈希并与期望比对。</summary>
    public static bool Verify(byte[] content, ZdHashAlgo algo, byte[] expected)
    {
        byte[] actual = Compute(content, algo);
        if (actual.Length != expected.Length) return false;
        for (int i = 0; i < actual.Length; i++)
            if (actual[i] != expected[i]) return false;
        return true;
    }
}