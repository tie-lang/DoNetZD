using System.Globalization;
using System.Text;

namespace DoNetZD;

/// <summary>
/// TOML ↔ ZdValue 互转。
/// 约定（对齐 TOML v1.0 常规子集）：
///   根 = Map；表 = Map；数组表 = Array[Map]；数组 = Array；
///   标量：整数/浮点/布尔/字符串；日期/时间按原样存为 String（zd 无日期标签）。
/// 解析覆盖：表 [a]、数组表 [[a]]、点分键 a.b、基本/字面量字符串、数组、内联表、注释。
/// </summary>
public static class TomlCodec
{
    /// <summary>TOML 文本 → ZdValue（根 Map）。</summary>
    public static ZdValue FromToml(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        var root = new Dictionary<string, object>(StringComparer.Ordinal);
        Dictionary<string, object> current = root;
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        foreach (string raw in lines)
        {
            string line = StripComment(raw).Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith("[[") && line.EndsWith("]]"))
            {
                string name = line.Substring(2, line.Length - 4).Trim();
                current = EnterArrayTable(root, SplitDotted(name));
            }
            else if (line[0] == '[' && line[line.Length - 1] == ']')
            {
                string name = line.Substring(1, line.Length - 2).Trim();
                current = NavigateDict(root, SplitDotted(name));
            }
            else
            {
                int eq = line.IndexOf('=');
                if (eq < 0)
                    throw new FormatException($"TOML 缺少 '='：{line}");
                string key = line.Substring(0, eq).Trim();
                object val = ParseValueString(line.Substring(eq + 1).Trim());
                var keyParts = SplitDotted(key);
                var target = NavigateParent(current, keyParts);
                target[keyParts[keyParts.Length - 1]] = val;
            }
        }
        return ZdValue.FromObject(root);
    }

    /// <summary>ZdValue（根 Map）→ TOML 文本。</summary>
    public static string ToToml(ZdValue value)
    {
        if (value is not ZdValue.Map root)
            throw new ArgumentException("期望根为 zd Map");
        var sb = new StringBuilder();
        EmitTableOrRoot(sb, root, "", isRoot: true);
        return sb.ToString();
    }

    // ---- 解析辅助 ----

    private static string StripComment(string line)
    {
        bool basic = false, literal = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '#' && !basic && !literal)
                return line.Substring(0, i).TrimEnd();
            if (c == '"' && !literal) basic = !basic;
            else if (c == '\'' && !basic) literal = !literal;
        }
        return line;
    }

    private static string[] SplitDotted(string key)
        => key.Split('.').Select(k => k.Trim().Trim('"', '\'')).Where(k => k.Length > 0).ToArray();

    private static Dictionary<string, object> NavigateDict(Dictionary<string, object> root, string[] path)
    {
        Dictionary<string, object> node = root;
        foreach (string part in path)
        {
            if (!node.TryGetValue(part, out object? child) || child is not Dictionary<string, object> d)
            {
                d = new Dictionary<string, object>(StringComparer.Ordinal);
                node[part] = d;
            }
            node = d;
        }
        return node;
    }

    /// <summary>导航到「最后一个分量」所在的那个字典（不把叶子当表）。</summary>
    private static Dictionary<string, object> NavigateParent(Dictionary<string, object> root, string[] path)
    {
        Dictionary<string, object> node = root;
        int depth = path.Length - 1;
        for (int i = 0; i < depth; i++)
        {
            if (!node.TryGetValue(path[i], out object? child) || child is not Dictionary<string, object> d)
            {
                d = new Dictionary<string, object>(StringComparer.Ordinal);
                node[path[i]] = d;
            }
            node = d;
        }
        return node;
    }

    private static Dictionary<string, object> EnterArrayTable(Dictionary<string, object> root, string[] path)
    {
        Dictionary<string, object> node = root;
        for (int i = 0; i < path.Length - 1; i++)
        {
            if (!node.TryGetValue(path[i], out object? child2) || child2 is not Dictionary<string, object> d)
            {
                d = new Dictionary<string, object>(StringComparer.Ordinal);
                node[path[i]] = d;
            }
            node = d;
        }
        string last = path[path.Length - 1];
        if (!node.TryGetValue(last, out object? arrObj) || arrObj is not List<object> arr)
        {
            arr = new List<object>();
            node[last] = arr;
        }
        var row = new Dictionary<string, object>(StringComparer.Ordinal);
        arr.Add(row);
        return row;
    }

    /// <summary>把 '=' 右侧的 TOML 值字面串解析为 CLR 对象。</summary>
    private static object ParseValueString(string s)
    {
        int i = 0;
        object v = ParseValue(s, ref i);
        return v;
    }

    private static object ParseValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length)
            return "";
        char c = s[i];
        if (c == '"')
            return ParseBasicString(s, ref i);
        if (c == '\'')
            return ParseLiteralString(s, ref i);
        if (c == '[')
            return ParseArray(s, ref i);
        if (c == '{')
            return ParseInlineTable(s, ref i);
        // 标量：读到空白 / 逗号 / 结束
        int start = i;
        while (i < s.Length && s[i] != ',' && s[i] != ']' && s[i] != '}' && s[i] != '#' && !char.IsWhiteSpace(s[i]))
            i++;
        string tok = s.Substring(start, i - start);
        return ParseScalar(tok);
    }

    private static object ParseScalar(string tok)
    {
        if (tok == "true") return true;
        if (tok == "false") return false;
        var clean = tok.Replace("_", "");
        // 整数（十进制 / hex / oct / bin）
        if (long.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
            return l;
        if (clean.Length > 2 && clean.StartsWith("0x") && long.TryParse(clean.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hx))
            return hx;
        if (clean.Length > 2 && clean.StartsWith("0o") && long.TryParse(clean.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out long oc)
            && IsAll(clean.Substring(2), c => c >= '0' && c <= '7'))
            return oc;
        if (clean.Length > 2 && clean.StartsWith("0b") && long.TryParse(clean.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out long bn)
            && IsAll(clean.Substring(2), c => c == '0' || c == '1'))
            return bn;
        if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out double dbl))
            if (tok.Contains('.') || tok.Contains('e') || tok.Contains('E') || tok.Contains("inf") || tok.Contains("nan"))
                return dbl;
        return tok; // 日期/时间或未知 → 字符串
    }

    private static bool IsAll(string s, Func<char, bool> pred)
    {
        if (s.Length == 0) return false;
        foreach (char c in s) if (!pred(c)) return false;
        return true;
    }

    private static object ParseArray(string s, ref int i)
    {
        i++; // '['
        var list = new List<object>();
        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("TOML 数组未闭合");
            if (s[i] == ']') { i++; return list; }
            list.Add(ParseValue(s, ref i));
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; return list; }
            throw new FormatException("TOML 数组期望 ',' 或 ']'");
        }
    }

    private static object ParseInlineTable(string s, ref int i)
    {
        i++; // '{'
        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("TOML 内联表未闭合");
            if (s[i] == '}') { i++; return map; }
            int ks = i;
            while (i < s.Length && s[i] != '=' && s[i] != '}') i++;
            string key = s.Substring(ks, i - ks).Trim().Trim('"', '\'');
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '=') throw new FormatException("TOML 内联表期望 '='");
            i++;
            SkipWs(s, ref i);
            map[key] = ParseValue(s, ref i);
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; return map; }
            throw new FormatException("TOML 内联表期望 ',' 或 '}'");
        }
    }

    private static string ParseBasicString(string s, ref int i)
    {
        i++; // 开引号
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c == '\\' && i < s.Length)
            {
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    case 'U': sb.Append(char.ConvertFromUtf32(int.Parse(s.Substring(i, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture))); i += 8; break;
                    default: sb.Append(e); break;
                }
            }
            else sb.Append(c);
        }
        throw new FormatException("TOML 基本字符串未闭合");
    }

    private static string ParseLiteralString(string s, ref int i)
    {
        i++; // 开引号
        int end = s.IndexOf('\'', i);
        if (end < 0) throw new FormatException("TOML 字面量字符串未闭合");
        string v = s.Substring(i, end - i);
        i = end + 1;
        return v;
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    // ---- 序列化 ----

    /// <summary>根：直接吐正文（无表头）。</summary>
    private static void EmitTableOrRoot(StringBuilder sb, ZdValue.Map map, string path, bool isRoot)
    {
        if (isRoot)
            EmitMapBody(sb, map, "");
        else
            EmitTable(sb, map, path);
    }

    /// <summary>打印 [path] 表头 + 正文（path 已按组件格式化，直接使用）。</summary>
    private static void EmitTable(StringBuilder sb, ZdValue.Map map, string path)
    {
        sb.Append('[').Append(path).Append("]\n");
        EmitMapBody(sb, map, path);
        sb.Append('\n');
    }

    /// <summary>数组表 [[path]]：每个 Map 行一段。</summary>
    private static void EmitArrayTables(StringBuilder sb, ZdValue.Array arr, string path)
    {
        foreach (ZdValue row in arr.Items)
        {
            if (row is not ZdValue.Map m) continue;
            sb.Append("[[").Append(path).Append("]]\n");
            EmitMapBody(sb, m, path);
            sb.Append('\n');
        }
    }

    /// <summary>表正文（无表头）：标量赋值 + 子表 + 数组表。</summary>
    private static void EmitMapBody(StringBuilder sb, ZdValue.Map map, string path)
    {
        foreach (var kv in map.Entries)
            if (IsScalarOrScalarArray(kv.Value))
                sb.Append(BareKey(kv.Key)).Append(" = ").Append(ScalarToml(kv.Value)).Append('\n');

        foreach (var kv in map.Entries)
            if (kv.Value is ZdValue.Map)
                EmitTable(sb, (ZdValue.Map)kv.Value, PathJoin(path, kv.Key));

        foreach (var kv in map.Entries)
            if (kv.Value is ZdValue.Array arr && arr.Items.Count > 0 && arr.Items[0] is ZdValue.Map)
                EmitArrayTables(sb, arr, PathJoin(path, kv.Key));
    }

    private static bool IsScalarOrScalarArray(ZdValue v)
        => v is not ZdValue.Map && (v is not ZdValue.Array a || a.Items.Count == 0 || a.Items[0] is not ZdValue.Map);

    private static string PathJoin(string basePath, string key)
        => basePath.Length == 0 ? KeyRef(key) : basePath + "." + KeyRef(key);

    private static string KeyRef(string key) => BareKey(key);

    private static string BareKey(string key)
        => IsBare(key) ? key : "\"" + key.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static bool IsBare(string key)
    {
        if (key.Length == 0) return false;
        foreach (char c in key)
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                return false;
        return true;
    }

    private static string ScalarToml(ZdValue v) => v switch
    {
        ZdValue.String s => Quote(s.Value),
        ZdValue.Integer i => i.Value.ToString(CultureInfo.InvariantCulture),
        ZdValue.Float f => f.Value.ToString("R", CultureInfo.InvariantCulture),
        ZdValue.Bool b => b.Value ? "true" : "false",
        ZdValue.Char c => Quote(char.ConvertFromUtf32(c.Codepoint)),
        ZdValue.Null => Quote(""),
        ZdValue.Trit t => t.Value.ToString(CultureInfo.InvariantCulture),
        ZdValue.Array a => "[" + string.Join(", ", a.Items.Select(x => ScalarToml(x))) + "]",
        _ => throw new ArgumentException($"TOML 值不支持 {v?.GetType().Name ?? "null"}"),
    };

    private static string Quote(string s)
        => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r") + "\"";
}