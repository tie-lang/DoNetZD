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
