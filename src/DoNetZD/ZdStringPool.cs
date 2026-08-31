using System.Collections.Generic;

namespace DoNetZD;

/// <summary>
/// v2 字符串池段管理器（语言无关：池 = 字符串表）。维护 字符串→uint 索引 的字典
/// 与 索引→字符串 的表。配合 <see cref="ZdCodec.EncodeWithPool"/> / 
/// <see cref="ZdCodec.DecodeWithPool"/> 实现字符串字典/引用，重复字符串只存一次。
/// </summary>
public sealed class ZdStringPool
{
    private readonly Dictionary<string, uint> _index = new();
    private readonly List<string> _table = new();

    /// <summary>已登记的唯一字符串数。</summary>
    public int Count => _table.Count;

    /// <summary>取内部有序字符串表（索引顺序，仅供语义展示/测试）。</summary>
    public IReadOnlyList<string> UniqueStrings => _table;

    /// <summary>登记字符串并返回其索引；已存在则复用。</summary>
    public uint Intern(string s)
    {
        if (s is null)
            s = string.Empty;
        if (_index.TryGetValue(s, out uint idx))
            return idx;
        idx = (uint)_table.Count;
        _index[s] = idx;
        _table.Add(s);
        return idx;
    }

    /// <summary>取字符串索引；未登记返回 uint.MaxValue。</summary>
    public uint GetIndex(string s)
    {
        if (s is null)
            s = string.Empty;
        return _index.TryGetValue(s, out uint idx) ? idx : uint.MaxValue;
    }

    /// <summary>按索引取字符串；越界返回 null。</summary>
    public string? Get(uint index)
        => index < _table.Count ? _table[(int)index] : null;

    /// <summary>按索引顺序追加字符串（供解码端重建池；调用方须保证顺序单调）。</summary>
    public void AddInOrder(string s)
    {
        s ??= string.Empty;
        uint idx = (uint)_table.Count;
        _index[s] = idx;
        _table.Add(s);
    }

    /// <summary>清空池。</summary>
    public void Clear()
    {
        _index.Clear();
        _table.Clear();
    }
}