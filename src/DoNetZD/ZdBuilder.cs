using System;

namespace DoNetZD;

/// <summary>
/// 单增（append-only）字节缓冲，供编码路径复用，消除连续 <see cref="Zd.Concat"/>
/// 拼接导致的 O(n²) 重复分配。原地扩容、支持 <see cref="Reserve"/> 预分配。
/// netstandard2.0、零依赖。
/// </summary>
public sealed class ZdBuilder
{
    private byte[] _buf;
    private int _len;

    /// <summary>以初始容量创建缓冲（默认 64）。</summary>
    public ZdBuilder(int capacity = 64)
    {
        _buf = new byte[Math.Max(capacity, 16)];
        _len = 0;
    }

    /// <summary>已写入的字节数。</summary>
    public int Length => _len;

    /// <summary>当前底层缓冲容量。</summary>
    public int Capacity => _buf.Length;

    /// <summary>确保还能再追加 count 字节（预分配，减少 realloc）。</summary>
    public void Reserve(int count)
    {
        int need = _len + count;
        if (need <= _buf.Length)
            return;
        int target = _buf.Length == 0 ? 16 : _buf.Length;
        while (target < need)
            target *= 2;
        Array.Resize(ref _buf, target);
    }

    /// <summary>追加一字节。</summary>
    public ZdBuilder AppendByte(byte b)
    {
        Reserve(1);
        _buf[_len++] = b;
        return this;
    }

    /// <summary>追加一段字节（块拷贝）。</summary>
    public ZdBuilder AppendBytes(byte[] bytes, int offset, int count)
    {
        if (bytes is null || count <= 0)
            return this;
        if (offset < 0 || count < 0 || offset + count > bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        Reserve(count);
        Buffer.BlockCopy(bytes, offset, _buf, _len, count);
        _len += count;
        return this;
    }

    /// <summary>追加整段字节。</summary>
    public ZdBuilder AppendBytes(byte[] bytes) => AppendBytes(bytes, 0, bytes?.Length ?? 0);

    /// <summary>拷贝返回已写入的字节数组。</summary>
    public byte[] ToArray()
    {
        var r = new byte[_len];
        Buffer.BlockCopy(_buf, 0, r, 0, _len);
        return r;
    }

    /// <summary>返回已写入字节（拷贝，与 <see cref="ToArray"/> 等价的安全形式）。</summary>
    public byte[] ToBytes() => ToArray();
}