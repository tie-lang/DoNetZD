using System.Globalization;
using System.Text;

namespace DoNetZD;

/// <summary>
/// YAML ↔ ZdValue 互转（常规子集）。
/// 解析覆盖：块映射 / 块序列 / 嵌套缩进；流式 [..]、{..}；单/双引号与未加引号标量；
/// 注释；块标量 | 与 &gt;（含 - 去除行尾换行）。不解析锚点/别名/标签/多行流式。
/// 标量类型：null(~ 空)、true/false、整数、浮点、字符串。
/// </summary>
public static class YamlCodec
{
    /// <summary>YAML 文本 → ZdValue。</summary>
    public static ZdValue FromYaml(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        var lines = Preprocess(text);
        int i = 0;
        if (i >= lines.Count)
            return new ZdValue.Map(new Dictionary<string, ZdValue>());
        ZdValue root = ParseBlock(lines, ref i, -1); // -1：根可从任意缩进开始
        return root;
    }

    /// <summary>ZdValue → YAML 文本（块式，带缩进）。</summary>
    public static string ToYaml(ZdValue value)
    {
        var sb = new StringBuilder();
        Write(sb, value, 0);
        return sb.ToString();
    }

    // ---- 预处理 ----

    private sealed class Line
    {
        public int Indent;
        public string Text = "";
    }

    private static List<Line> Preprocess(string text)
    {
        var lines = new List<Line>();
        string[] raw = text.Replace("\r\n", "\n").Replace('\t', ' ').Split('\n');
        foreach (string r in raw)
        {
            int indent = 0;
            while (indent < r.Length && r[indent] == ' ') indent++;
            string content = StripComment(r).Trim();
            if (content.Length == 0)
                continue; // 跳过空行 / 纯注释
            lines.Add(new Line { Indent = indent, Text = content });
        }
        return lines;
    }

    private static string StripComment(string line)
    {
        bool dq = false, sq = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"' && !sq) dq = !dq;
            else if (c == '\'' && !dq) sq = !sq;
            else if (c == '#' && !dq && !sq && (i == 0 || line[i - 1] == ' '))
                return line.Substring(0, i);
        }
        return line;
    }

    // ---- 解析 ----

    private static ZdValue ParseBlock(List<Line> lines, ref int i, int parentIndent)
    {
        if (i >= lines.Count) return new ZdValue.String("");
        Line line = lines[i];
        if (line.Indent <= parentIndent)
            return new ZdValue.String(""); // 不归一层的哨兵
        if (line.Text.StartsWith("-"))
            return ParseSequence(lines, ref i, line.Indent);
        int colon = TopLevelColon(line.Text);
        if (colon >= 0)
            return ParseMapping(lines, ref i, line.Indent);
        // 标量/流式
        i++;
        return ParseScalarOrFlow(line.Text);
    }

    private static ZdValue ParseMapping(List<Line> lines, ref int i, int indent, string? seed = null)
    {
        var map = new Dictionary<string, ZdValue>(StringComparer.Ordinal);
        if (seed != null)
        {
            (string key, string rest) = SplitColon(seed);
            map[key] = ParseMappingValue(lines, ref i, indent, rest);
        }
        while (i < lines.Count)
        {
            Line line = lines[i];
            if (line.Indent != indent || line.Text.StartsWith("-"))
                break;
            int colon = TopLevelColon(line.Text);
            if (colon < 0)
                break;
            i++;
            (string k, string v) = SplitColon(line.Text);
            map[k] = ParseMappingValue(lines, ref i, indent, v);
        }
        return new ZdValue.Map(map);
    }

    private static ZdValue ParseMappingValue(List<Line> lines, ref int i, int indent, string rest)
    {
        // 块标量：| 或 >
        if (rest == "|" || rest == ">" || rest == "|-" || rest == ">-")
            return ParseBlockScalar(lines, ref i, indent, rest);
        if (rest.Length == 0)
            return ParseBlock(lines, ref i, indent); // 嵌套块
        return ParseScalarOrFlow(rest);
    }

    private static ZdValue ParseSequence(List<Line> lines, ref int i, int indent)
    {
        var items = new List<ZdValue>();
        while (i < lines.Count)
        {
            Line line = lines[i];
            if (line.Indent != indent || !line.Text.StartsWith("-"))
                break;
            i++;
            string rest = line.Text.Substring(1).Trim();
            if (rest.Length == 0)
            {
                items.Add(ParseBlock(lines, ref i, indent));
            }
            else if (TopLevelColon(rest) >= 0)
            {
                // 序列项是一个映射：键始于 "- " 后的列（indent+2）
                items.Add(ParseMapping(lines, ref i, indent + 2, seed: rest));
            }
            else
            {
                items.Add(ParseScalarOrFlow(rest));
            }
        }
        return new ZdValue.Array(items);
    }

    private static ZdValue ParseBlockScalar(List<Line> lines, ref int i, int indent, string marker)
    {
        var sb = new StringBuilder();
        bool folded = marker.StartsWith(">");
        int firstIndent = -1;
        while (i < lines.Count)
        {
            Line line = lines[i];
            if (line.Indent <= indent)
                break;
            if (firstIndent < 0) firstIndent = line.Indent;
            int keep = line.Indent - firstIndent;
            if (folded && sb.Length > 0 && !sb.ToString().EndsWith("\n"))
                sb.Append(' ');
            sb.Append(new string(' ', keep)).Append(line.Text);
            // 块标量每行以换行结束（literal 保留；folded 折叠）
            if (i + 1 < lines.Count && lines[i + 1].Indent > indent)
                sb.Append('\n');
            i++;
        }
        string val = sb.ToString();
        if (marker.EndsWith("-"))
            val = val.TrimEnd('\n');
        return new ZdValue.String(val);
    }

    /// <summary>标量 / 流式标量或容器。</summary>
    private static ZdValue ParseScalarOrFlow(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return ZdValue.Null.Instance;
        if (text.StartsWith("[") || text.StartsWith("{"))
            return ParseFlow(text);
        if (text[0] == '"')
            return new ZdValue.String(ParseDoubleQuoted(text));
        if (text[0] == '\'')
            return new ZdValue.String(ParseSingleQuoted(text));
        // 裸标量
        if (text == "null" || text == "~" || text == "Null" || text == "NULL")
            return ZdValue.Null.Instance;
        if (text == "true" || text == "True" || text == "TRUE")
            return new ZdValue.Bool(true);
        if (text == "false" || text == "False" || text == "FALSE")
            return new ZdValue.Bool(false);
        var clean = text.Replace("_", "");
        if (long.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
            return new ZdValue.Integer(l);
        if ((clean.Contains('.') || clean.Contains('e') || clean.Contains('E'))
            && double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            return new ZdValue.Float(d);
        return new ZdValue.String(text);
    }

    // ---- 流式解析（在单行内的 [ ... ] / { ... }）----
    private static ZdValue ParseFlow(string line)
    {
        int i = 0;
        return ParseFlowValue(line, ref i);
    }

    private static ZdValue ParseFlowValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length) return ZdValue.Null.Instance;
        char c = s[i];
        if (c == '[')
        {
            i++;
            var items = new List<ZdValue>();
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("YAML 流式数组未闭合");
                if (s[i] == ']') { i++; return new ZdValue.Array(items); }
                // 空项 = null
                int save = i;
                SkipNonDelim(s, ref i);
                if (i == save && s[i] != ']')
                {
                    items.Add(ParseFlowValue(s, ref i));
                }
                else
                {
                    string tok = s.Substring(save, i - save);
                    if (tok.Length > 0)
                        items.Add(ParseScalarOrFlow(tok.Trim()));
                    else
                        items.Add(ZdValue.Null.Instance);
                }
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') i++;
            }
        }
        if (c == '{')
        {
            i++;
            var map = new Dictionary<string, ZdValue>(StringComparer.Ordinal);
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("YAML 流式映射未闭合");
                if (s[i] == '}') { i++; return new ZdValue.Map(map); }
                int ks = i;
                while (i < s.Length && s[i] != ':' && s[i] != ',' && s[i] != '}') i++;
                string key = s.Substring(ks, i - ks).Trim();
                if (key.Length > 0 && (key[0] == '"' || key[0] == '\''))
                    key = (key[0] == '"') ? ParseDoubleQuoted(key) : ParseSingleQuoted(key);
                if (i < s.Length && s[i] == ':')
                {
                    i++;
                    SkipWs(s, ref i);
                    int vs = i;
                    while (i < s.Length && s[i] != ',' && s[i] != '}') i++;
                    string vtok = s.Substring(vs, i - vs).Trim();
                    map[key] = vtok.Length > 0 ? ParseScalarOrFlow(vtok) : ZdValue.Null.Instance;
                }
                else
                {
                    map[key] = ZdValue.Null.Instance;
                }
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') i++;
            }
        }
        // 标量
        int st = i;
        while (i < s.Length && s[i] != ',' && s[i] != ']' && s[i] != '}') i++;
        return ParseScalarOrFlow(s.Substring(st, i - st));
    }

    private static void SkipNonDelim(string s, ref int i)
    {
        while (i < s.Length && s[i] != ',' && s[i] != ']' && s[i] != '}')
        {
            if (s[i] == '"' || s[i] == '\'')
            {
                char q = s[i++];
                while (i < s.Length && s[i] != q) i++;
                i++;
            }
            else i++;
        }
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    private static string ParseDoubleQuoted(string s)
    {
        // 输入以 " 开头；解析到匹配的闭合引号
        int i = 1;
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
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    default: sb.Append(e); break;
                }
            }
            else sb.Append(c);
        }
        throw new FormatException("YAML 双引号字符串未闭合");
    }

    private static string ParseSingleQuoted(string s)
    {
        int i = 1;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '\'')
            {
                if (i < s.Length && s[i] == '\'') { sb.Append('\''); i++; continue; }
                return sb.ToString();
            }
            sb.Append(c);
        }
        throw new FormatException("YAML 单引号字符串未闭合");
    }

    // ---- 冒号判断 / 分割 ----

    /// <summary>返回文本内「键:值」分隔冒号的下标（引号外、'. '后为空白或行尾），否则 -1。
    /// 规避 http:// 之类含冒号但不带空格的标量。</summary>
    private static int TopLevelColon(string text)
    {
        bool dq = false, sq = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' && !sq) dq = !dq;
            else if (c == '\'' && !dq) sq = !sq;
            else if (c == ':' && !dq && !sq)
            {
                if (i + 1 >= text.Length || text[i + 1] == ' ' || text[i + 1] == '\t')
                    return i;
            }
        }
        return -1;
    }

    private static (string Key, string Value) SplitColon(string text)
    {
        int colon = TopLevelColon(text);
        string key = colon >= 0 ? text.Substring(0, colon).Trim() : text.Trim();
        string value = colon >= 0 ? text.Substring(colon + 1).Trim() : "";
        if (key.Length > 0 && (key[0] == '"' || key[0] == '\''))
            key = (key[0] == '"') ? ParseDoubleQuoted(key) : ParseSingleQuoted(key);
        return (key, value);
    }

    // ---- 序列化 ----

    private static void Write(StringBuilder sb, ZdValue v, int indent)
    {
        switch (v)
        {
            case ZdValue.Map m:
                bool first = true;
                foreach (var kv in m.Entries)
                {
                    if (!first) sb.Append('\n');
                    first = false;
                    Indent(sb, indent);
                    string key = kv.Key;
                    if (NeedsQuote(key)) key = QuoteYaml(key);
                    sb.Append(key).Append(':');
                    WriteValueAfter(sb, kv.Value, indent);
                }
                break;
            case ZdValue.Array a:
                foreach (ZdValue item in a.Items)
                {
                    Indent(sb, indent);
                    sb.Append('-');
                    WriteItemAfter(sb, item, indent);
                }
                break;
            default:
                sb.Append(ScalarYaml(v));
                break;
        }
    }

    private static void WriteValueAfter(StringBuilder sb, ZdValue v, int indent)
    {
        switch (v)
        {
            case ZdValue.Map m:
                if (m.Entries.Count == 0) { sb.Append(" {}\n"); }
                else { sb.Append('\n'); Write(sb, m, indent + 2); }
                break;
            case ZdValue.Array a:
                if (a.Items.Count == 0) { sb.Append(" []\n"); }
                else { sb.Append('\n'); Write(sb, a, indent + 2); }
                break;
            default:
                sb.Append(' ').Append(ScalarYaml(v)).Append('\n');
                break;
        }
    }

    private static void WriteItemAfter(StringBuilder sb, ZdValue v, int indent)
    {
        switch (v)
        {
            case ZdValue.Map m:
                if (m.Entries.Count == 0) { sb.Append(" {}\n"); }
                else { MapFirstInline(sb, m, indent + 2); }
                break;
            case ZdValue.Array a:
                if (a.Items.Count == 0) { sb.Append(" []\n"); }
                else { sb.Append('\n'); Write(sb, a, indent + 2); }
                break;
            default:
                sb.Append(' ').Append(ScalarYaml(v)).Append('\n');
                break;
        }
    }

    private static void MapFirstInline(StringBuilder sb, ZdValue m, int indent)
    {
        var map = (ZdValue.Map)m;
        bool first = true;
        foreach (var kv in map.Entries)
        {
            if (!first) { sb.Append('\n'); Indent(sb, indent); }
            first = false;
            string key = kv.Key;
            if (NeedsQuote(key)) key = QuoteYaml(key);
            sb.Append(key).Append(':');
            WriteValueAfter(sb, kv.Value, indent);
        }
    }

    private static void Indent(StringBuilder sb, int n)
    {
        for (int i = 0; i < n; i++) sb.Append(' ');
    }

    private static string ScalarYaml(ZdValue v) => v switch
    {
        ZdValue.String s => NeedsQuote(s.Value) ? QuoteYaml(s.Value) : s.Value,
        ZdValue.Integer i => i.Value.ToString(CultureInfo.InvariantCulture),
        ZdValue.Float f => f.Value.ToString("R", CultureInfo.InvariantCulture),
        ZdValue.Bool b => b.Value ? "true" : "false",
        ZdValue.Char c => QuoteYaml(char.ConvertFromUtf32(c.Codepoint)),
        ZdValue.Trit t => t.Value.ToString(CultureInfo.InvariantCulture),
        ZdValue.Null => "null",
        _ => "",
    };

    private static bool NeedsQuote(string s)
    {
        if (s.Length == 0) return true;
        foreach (char c in s)
            if (c == ':' || c == '\n' || c == '{' || c == '}' || c == '[' || c == ']' || c == ',' || c == '#' || c == '&' || c == '*' || c == '!' || c == '|' || c == '>' || c == '\'' || c == '"')
                return true;
        if (s != s.Trim()) return true;
        // 裸标量易被判成其他类型时加引号
        if (s == "true" || s == "false" || s == "null" || s == "~") return true;
        return s.Length > 0 && char.IsDigit(s[0]);
    }

    private static string QuoteYaml(string s)
        => "'" + s.Replace("'", "''") + "'";
}