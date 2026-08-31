using System.Collections.Generic;

namespace DoNetZD;

/// <summary>v2 容器写入选项（决定 flags 位与附着段）。</summary>
public sealed class ZdV2Options
{
    /// <summary>字符串字典/引用（bit0）：用 <see cref="ZdCodec.EncodeWithPool"/>。池可复用，见 <see cref="Pool"/>。</summary>
    public bool UseStringDict;
    /// <summary>附着 schema 段（bit5）。</summary>
    public bool IncludeSchema;
    /// <summary>附着内容哈希段（bit6）。</summary>
    public bool IncludeHash;
    /// <summary>内容哈希算法（默认 CRC32）。</summary>
    public ZdHashAlgo HashAlgo = ZdHashAlgo.Crc32;
    /// <summary>schema 字段 (字段名, 类型)。</summary>
    public IReadOnlyList<KeyValuePair<string, string>>? Schema;
    /// <summary>复用/输出的字符串池（UseStringDict 时）。</summary>
    public ZdStringPool? Pool;
    /// <summary>强制标记列式位（bit1）；缺省按值内容自动检测。</summary>
    public bool ForceColumnar;
    /// <summary>强制标记 ext 位（bit2）；缺省按值内容自动检测。</summary>
    public bool ForceExt;
    /// <summary>流式分块位（bit3）。</summary>
    public bool Stream;
    /// <summary>压缩变体位（bit4）。</summary>
    public bool Compressed;
}

/// <summary>v2 容器解码结果元信息。</summary>
public sealed class ZdV2Result
{
    /// <summary>头部 flags 字节。</summary>
    public byte Flags;
    /// <summary>schema 段（若携带）。</summary>
    public IReadOnlyList<KeyValuePair<string, string>>? Schema;
    /// <summary>内容哈希算法（若携带）。</summary>
    public ZdHashAlgo? HashAlgo;
    /// <summary>内容哈希字节（若携带）。</summary>
    public byte[]? ContentHash;
    /// <summary>哈希校验是否通过（携带哈希段时）。</summary>
    public bool HashVerified;
    /// <summary>哈希校验失败原因（校验失败时）。</summary>
    public string? HashError;
    /// <summary>字典解码时使用的字符串池。</summary>
    internal ZdStringPool? StringPoolForDict;
}

/// <summary>
/// v2 容器写入/读取流程：携带 flags 头 + 可选 schema 段 + 内容哈希段。
/// 布局：<c>[v2头 flags][schema段?][hash段?][正文]</c>；哈希覆盖正文。
/// </summary>
public static class ZdV2
{
    /// <summary>把值编码为完整 v2 文件字节（含 10 字节头）。</summary>
    public static byte[] Encode(ZdValue v, ZdV2Options? options = null)
    {
        if (v is null) throw new ArgumentNullException(nameof(v));
        options ??= new ZdV2Options();

        byte[] valueBytes = options.UseStringDict
            ? ZdCodec.EncodeWithPool(v, options.Pool)
            : ZdCodec.Encode(v);

        byte flags = 0;
        if (options.UseStringDict) flags |= Zd.FlagDict;
        if (options.Stream) flags |= Zd.FlagStream;
        if (options.Compressed) flags |= Zd.FlagCompressed;
        if (options.IncludeSchema) flags |= Zd.FlagSchema;
        if (options.IncludeHash) flags |= Zd.FlagHash;
        if (options.ForceColumnar || ContainType(v, IsColumnar)) flags |= Zd.FlagColumnar;
        if (options.ForceExt || ContainType(v, IsExt)) flags |= Zd.FlagExt;

        var body = new ZdBuilder(valueBytes.Length + 96);
        if (options.IncludeSchema)
            body.AppendBytes(ZdSchema.Encode(options.Schema));
        if (options.IncludeHash)
            body.AppendBytes(ZdContentHash.Encode(valueBytes, options.HashAlgo));
        body.AppendBytes(valueBytes);

        byte[] header = Zd.V2Header(flags);
        var full = new byte[header.Length + body.Length];
        Buffer.BlockCopy(header, 0, full, 0, header.Length);
        Buffer.BlockCopy(body.ToArray(), 0, full, header.Length, body.Length);
        return full;
    }

    /// <summary>读取完整 v2 文件字节并解码，附元信息（<paramref name="result"/> 可空，未解码由用 Save/Load 拆分）。</summary>
    public static ZdValue Decode(byte[] data, ZdV2Result? result = null)
    {
        if (result is null) result = new ZdV2Result();
        result.Flags = Zd.GetFlags(data);
        if (Zd.DetectVersion(data) != ZdVersion.V2)
            throw new ZdFormatException("非 v2 容器", 0);

        int pos = 10;
        if ((result.Flags & Zd.FlagSchema) != 0)
            result.Schema = ZdSchema.Decode(data, ref pos);
        if ((result.Flags & Zd.FlagHash) != 0)
        {
            ZdContentHash.Decode(data, ref pos, out ZdHashAlgo algo, out byte[] hash);
            result.HashAlgo = algo;
            result.ContentHash = hash;
            byte[] content = Slice(data, pos, data.Length - pos);
            if (ZdContentHash.Verify(content, algo, hash))
            {
                result.HashVerified = true;
            }
            else
            {
                result.HashVerified = false;
                result.HashError = "内容哈希校验失败：数据已被改动";
            }
        }
        byte[] valueBytes = Slice(data, pos, data.Length - pos);
        return (result.Flags & Zd.FlagDict) != 0
            ? ZdCodec.DecodeWithPool(valueBytes, result.StringPoolForDict)
            : ZdCodec.Decode(valueBytes);
    }

    private static byte[] Slice(byte[] d, int off, int len)
    {
        var r = new byte[len];
        System.Buffer.BlockCopy(d, off, r, 0, len);
        return r;
    }

    private static bool IsColumnar(ZdValue v) => v is ZdValue.Columnar;
    private static bool IsExt(ZdValue v) => v is ZdValue.Ext;

    private static bool ContainType(ZdValue v, System.Func<ZdValue, bool> pred)
    {
        bool found = false;
        v.Visit(x => { if (pred(x)) found = true; });
        return found;
    }
}