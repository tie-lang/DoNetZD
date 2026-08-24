using System.Globalization;
using System.Text;

namespace DoNetZD;

/// <summary>
/// JSON ↔ ZdValue 互转。
/// 映射：object→Map、array→Array、string→String、true/false→Bool、
/// 整数→Integer、含小数/指数→Float；JSON null→Null 哨兵。
/// 注意 zd 字节编码不支持 null（见 ZdValue.Null 说明）。
/// </summary>
public static class JsonCodec
{
    // ==================== 序列化（ZdValue → JSON 文本）====================

    /// <summary>把 zd 值序列化为 JSON 字符串（可选 pretty 缩进）。</summary>
    public static string Serialize(ZdValue value, bool pretty = false)
    {
        var sb = new StringBuilder();
        Write(sb, value, pretty, 0);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, ZdValue value, bool pretty, int indent)
    {
        if (pretty) Newline(sb, indent);
        switch (value)
        {
            case ZdValue.Integer i: sb.Append(i.Value.ToString(CultureInfo.InvariantCulture)); break;
            case ZdValue.Float f: sb.Append(FloatText(f.Value)); break;
            case ZdValue.Bool b: sb.Append(b.Value ? "true" : "false"); break;
            case ZdValue.Char c: sb.Append('"').Append(Escape(char.ConvertFromUtf32(c.Codepoint))).Append('"'); break;
            case ZdValue.String s: sb.Append('"').Append(Escape(s.Value)).Append('"'); break;
            case ZdValue.Null: sb.Append("null"); break;
            case ZdValue.Trit t: sb.Append(t.Value.ToString(CultureInfo.InvariantCulture)); break;
            case ZdValue.Array a:
                sb.Append('[');
                for (int i = 0; i < a.Items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Write(sb, a.Items[i], pretty, indent + 1);
                }
                if (pretty) Newline(sb, indent);
                sb.Append(']');
                break;
            case ZdValue.Map m:
                sb.Append('{');
                int n = 0;
                foreach (var kv in m.Entries)
                {
                    if (n++ > 0) sb.Append(',');
                    if (pretty) Newline(sb, indent + 1);
                    sb.Append('"').Append(Escape(kv.Key)).Append("\":");
                    Write(sb, kv.Value, pretty, indent + 1);
                }
                if (pretty) Newline(sb, indent);
                sb.Append('}');
                break;
            default:
                throw new ArgumentException($"未知 zd 值类型 {value.GetType().Name}");
        }
    }

    private static void Newline(StringBuilder sb, int indent)
    {
        sb.Append('\n');
        for (int i = 0; i < indent; i++)
            sb.Append("  ");
    }

    private static string FloatText(double d)
    {
        if (double.IsNaN(d)) return "null";
        if (double.IsPositiveInfinity(d)) return "null";
        if (double.IsNegativeInfinity(d)) return "null";
        return d.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    // ==================== 解析（JSON 文本 → ZdValue）====================

    /// <summary>解析 JSON 文本为 zd 值。语法错误抛 Exception。</summary>
    public static ZdValue Parse(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        int pos = 0;
        ZdValue value = ParseValue(text, ref pos);
        SkipWs(text, ref pos);
        if (pos < text.Length)
            throw Format($"JSON 尾部存在多余内容 '{text[pos]}'", pos);
        return value;
    }

    private static ZdValue ParseValue(string s, ref int pos)
    {
        SkipWs(s, ref pos);
        if (pos >= s.Length)
            throw Format("JSON 意外结束", pos);
        switch (s[pos])
        {
            case '{': return ParseObject(s, ref pos);
            case '[': return ParseArray(s, ref pos);
            case '"': return new ZdValue.String(ParseString(s, ref pos));
            case 't': Expect(s, ref pos, "true"); return new ZdValue.Bool(true);
            case 'f': Expect(s, ref pos, "false"); return new ZdValue.Bool(false);
            case 'n': Expect(s, ref pos, "null"); return ZdValue.Null.Instance;
            default: return ParseNumber(s, ref pos);
        }
    }

    private static ZdValue ParseObject(string s, ref int pos)
    {
        Move(s, ref pos, '{');
        SkipWs(s, ref pos);
        var map = new Dictionary<string, ZdValue>();
        if (Peek(s, ref pos) == '}')
        {
            Move(s, ref pos, '}');
            return new ZdValue.Map(map);
        }
        while (true)
        {
            SkipWs(s, ref pos);
            string key = ParseString(s, ref pos);
            SkipWs(s, ref pos);
            Move(s, ref pos, ':');
            ZdValue value = ParseValue(s, ref pos);
            map[key] = value;
            SkipWs(s, ref pos);
            char c = Peek(s, ref pos);
            if (c == ',') { pos++; continue; }
            if (c == '}') { pos++; break; }
            throw Format($"JSON 对象期望 ',' 或 '}}'，实际 '{c}'", pos);
        }
        return new ZdValue.Map(map);
    }

    private static ZdValue ParseArray(string s, ref int pos)
    {
        Move(s, ref pos, '[');
        SkipWs(s, ref pos);
        var items = new List<ZdValue>();
        if (Peek(s, ref pos) == ']')
        {
            pos++;
            return new ZdValue.Array(items);
        }
        while (true)
        {
            items.Add(ParseValue(s, ref pos));
            SkipWs(s, ref pos);
            char c = Peek(s, ref pos);
            if (c == ',') { pos++; continue; }
            if (c == ']') { pos++; break; }
            throw Format($"JSON 数组期望 ',' 或 ']'，实际 '{c}'", pos);
        }
        return new ZdValue.Array(items);
    }

    private static ZdValue ParseNumber(string s, ref int pos)
    {
        int start = pos;
        int i = pos;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        bool isFloat = false;
        int digitStart = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        if (i == digitStart)
            throw Format("JSON 数字无有效数字", pos);
        if (i < s.Length && s[i] == '.')
        {
            isFloat = true;
            i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        }
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            isFloat = true;
            i++;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        }
        string token = s.Substring(start, i - start);
        pos = i;
        if (isFloat)
            return new ZdValue.Float(double.Parse(token, CultureInfo.InvariantCulture));
        if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
            return new ZdValue.Integer(l);
        // 超出 long 范围：当作 double
        return new ZdValue.Float(double.Parse(token, CultureInfo.InvariantCulture));
    }

    private static string ParseString(string s, ref int pos)
    {
        Move(s, ref pos, '"');
        var sb = new StringBuilder();
        while (true)
        {
            if (pos >= s.Length)
                throw Format("JSON 字符串未闭合", pos);
            char c = s[pos++];
            if (c == '"')
                return sb.ToString();
            if (c == '\\')
            {
                if (pos >= s.Length)
                    throw Format("JSON 转义不完整", pos);
                char e = s[pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 > s.Length)
                            throw Format("JSON \\u 转义不完整", pos);
                        int code = int.Parse(s.Substring(pos, 4), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        pos += 4;
                        sb.Append((char)code);
                        break;
                    default: throw Format($"JSON 非法转义 \\{e}", pos);
                }
            }
            else
            {
                sb.Append(c);
            }
        }
    }

    // ---- 扫描辅助 ----

    private static void SkipWs(string s, ref int pos)
    {
        while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\n' || s[pos] == '\r'))
            pos++;
    }

    private static char Peek(string s, ref int pos) => pos < s.Length ? s[pos] : '\0';

    private static void Move(string s, ref int pos, char expected)
    {
        if (pos >= s.Length || s[pos] != expected)
            throw Format($"期望 '{expected}'，实际 '{(pos < s.Length ? s[pos] : '$')}'", pos);
        pos++;
    }

    private static void Expect(string s, ref int pos, string word)
    {
        if (pos + word.Length > s.Length || s.Substring(pos, word.Length) != word)
            throw Format($"期望 '{word}'", pos);
        pos += word.Length;
    }

    private static Exception Format(string message, int pos)
        => new FormatException($"JSON 解析错误：{message}（位置 {pos}）");
}