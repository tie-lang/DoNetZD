using System.Collections.Generic;

namespace DoNetZD;

/// <summary>
/// 简单路径访问：点分键 + [索引]，如 "users[0].name"、"a.b[2].c"。
/// 规则：
///   - 可选 '$' 前缀（自动忽略）；
///   - 键为点分段，支持 "a.b.c"；索引为方括号整数 "[3]" 或带引号键 "['key']"/"["key"]"；
///   - 不支持通配 / 过滤器 / 递归下降。
/// 找不到返回 null（<see cref="TryGet"/> 返回 false）。
/// </summary>
public static class ZdPath
{
    /// <summary>按路径取值；不存在返回 null。</summary>
    public static ZdValue? Get(ZdValue? root, string path)
    {
        TryGet(root, path, out ZdValue? v);
        return v;
    }

    /// <summary>按路径取值；返回是否命中。</summary>
    public static bool TryGet(ZdValue? root, string path, out ZdValue? value)
    {
        value = null;
        if (root is null)
            return false;
        if (string.IsNullOrEmpty(path))
        {
            value = root;
            return true;
        }

        ZdValue? cur = root;
        foreach (Seg seg in Parse(path))
        {
            if (cur is null)
                return false;
            switch (seg)
            {
                case Key k:
                    if (cur is ZdValue.Map m && m.Entries.TryGetValue(k.Name, out ZdValue? mv))
                        cur = mv;
                    else
                        return false;
                    break;
                case Index ix:
                    if (cur is ZdValue.Array a && ix.I >= 0 && ix.I < a.Items.Count)
                        cur = a.Items[ix.I];
                    else
                        return false;
                    break;
            }
        }
        value = cur;
        return cur != null;
    }

    // ==================== 路径写回（Set / Mutate）====================

    /// <summary>
    /// 在路径处写入值，返回新的根（不可变重建，原根不变；写回用
    /// <c>root = ZdPath.Set(root, path, value);</c>）。
    /// 规则：
    ///   - 空路径等价于整体替换根；
    ///   - Map 键：叶子键新增/替换；中间键缺失时自动创建空 Map（建链）；
    ///   - Array 索引：叶子处 i==Count 追加、i&lt;Count 替换、i&gt;Count 越界失败；
    ///     中间处 i 越界失败；
    ///   - 段类型不匹配（Key 落在 Array / Index 落在 Map）失败。
    /// 失败抛 <see cref="InvalidOperationException"/>；需要忽略失败用 <see cref="TrySet"/>。
    /// </summary>
    public static ZdValue Set(ZdValue root, string path, ZdValue value)
    {
        if (!TrySet(root, path, value, out ZdValue? result))
            throw new InvalidOperationException($"路径不可写：{path}");
        return result!;
    }

    /// <summary>按路径写回；成功返回 true 并把新根写入 <paramref name="result"/>。</summary>
    public static bool TrySet(ZdValue root, string path, ZdValue value, out ZdValue? result)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));
        if (string.IsNullOrEmpty(path))
        {
            result = value;
            return true;
        }
        Seg[] segs = Parse(path).ToArray();
        if (segs.Length == 0)
        {
            result = value;
            return true;
        }
        ZdValue? r = SetCore(root, segs, 0, value);
        result = r;
        return r != null;
    }

    private static ZdValue? SetCore(ZdValue? node, Seg[] segs, int idx, ZdValue value)
    {
        bool last = idx == segs.Length - 1;
        switch (segs[idx])
        {
            case Key key:
                ZdValue.Map? oldMap = node as ZdValue.Map;
                var nm = new Dictionary<string, ZdValue>();
                if (oldMap != null)
                {
                    foreach (var kv in oldMap.Entries)
                        nm[kv.Key] = kv.Value;
                }
                else if (node != null)
                {
                    return null;   // 现有节点不是 Map → 失败
                }
                if (last)
                {
                    nm[key.Name] = value;
                    return new ZdValue.Map(nm);
                }
                if (!nm.TryGetValue(key.Name, out ZdValue? sub))
                {
                    sub = new ZdValue.Map(new Dictionary<string, ZdValue>());
                    nm[key.Name] = sub;
                }
                ZdValue? newSub = SetCore(sub, segs, idx + 1, value);
                if (newSub == null)
                    return null;
                nm[key.Name] = newSub;
                return new ZdValue.Map(nm);
            case Index ix:
                if (node is not ZdValue.Array a)
                    return null;
                var items = new List<ZdValue>(a.Items);
                if (last)
                {
                    if (ix.I < 0 || ix.I > items.Count)
                        return null;                 // 越界（允许 == Count 追加）
                    if (ix.I == items.Count)
                        items.Add(value);
                    else
                        items[ix.I] = value;
                    return new ZdValue.Array(items);
                }
                if (ix.I < 0 || ix.I >= items.Count)
                    return null;
                ZdValue? newItem = SetCore(items[ix.I], segs, idx + 1, value);
                if (newItem == null)
                    return null;
                items[ix.I] = newItem;
                return new ZdValue.Array(items);
            default:
                return null;
        }
    }

    private abstract class Seg { }
    private sealed class Key : Seg { public string Name; public Key(string n) => Name = n; }
    private sealed class Index : Seg { public int I; public Index(int i) => I = i; }

    private static IEnumerable<Seg> Parse(string path)
    {
        string p = path.Trim();
        if (p.StartsWith("$"))
            p = p.Substring(1).TrimStart('.');

        int i = 0;
        while (i < p.Length)
        {
            if (p[i] == '.')
            {
                i++;
                continue;
            }
            if (p[i] == '[')
            {
                int j = p.IndexOf(']', i + 1);
                if (j < 0)
                    throw new FormatException($"路径索引未闭合：{path}");
                string inner = p.Substring(i + 1, j - i - 1).Trim();
                if (inner.Length >= 2 && (inner[0] == '"' || inner[0] == '\''))
                {
                    yield return new Key(inner.Substring(1, inner.Length - 2));
                }
                else
                {
                    if (!int.TryParse(inner, out int idx))
                        throw new FormatException($"路径索引非数字：{inner}");
                    yield return new Index(idx);
                }
                i = j + 1;
            }
            else
            {
                int nextDot = p.IndexOf('.', i);
                int nextBrk = p.IndexOf('[', i);
                int end;
                if (nextDot < 0 && nextBrk < 0) end = p.Length;
                else if (nextDot < 0) end = nextBrk;
                else if (nextBrk < 0) end = nextDot;
                else end = nextDot < nextBrk ? nextDot : nextBrk;
                string key = p.Substring(i, end - i);
                if (key.Length > 0)
                    yield return new Key(key);
                i = end;
            }
        }
    }
}
