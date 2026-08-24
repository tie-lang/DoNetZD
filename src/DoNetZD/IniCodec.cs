using System.Text;

namespace DoNetZD;

/// <summary>
/// INI ↔ ZdValue 互转。
/// 约定：Map{ 段名: Map{ 键: 值(String) } }；段名 "" 表示文件头部（无 [段] 的全局键）。
/// 注释行为以 ';' 或 '#' 开头。值：转为 zd 时统一 String；读回默认也 String。
/// </summary>
public static class IniCodec
{
    /// <summary>INI 文本 → zd 嵌套 Map（段 → 键值 Map）。</summary>
    public static ZdValue FromIni(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        string current = ""; // 全局段
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                continue;
            if (line[0] == '[')
            {
                int end = line.IndexOf(']');
                if (end > 0)
                    current = line.Substring(1, end - 1).Trim();
                continue;
            }
            int eq = line.IndexOf('=');
            string key = eq >= 0 ? line.Substring(0, eq).Trim() : line.Trim();
            string val = eq >= 0 ? line.Substring(eq + 1).Trim() : "";
            if (key.Length == 0)
                continue;
            if (!sections.TryGetValue(current, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                sections[current] = map;
            }
            map[key] = val;
        }
        var entries = new Dictionary<string, ZdValue>();
        foreach (var kv in sections)
        {
            entries[kv.Key] = new ZdValue.Map(
                kv.Value.ToDictionary(p => p.Key, p => (ZdValue)new ZdValue.String(p.Value), StringComparer.Ordinal));
        }
        return new ZdValue.Map(entries);
    }

    /// <summary>zd 嵌套 Map → INI 文本。</summary>
    public static string ToIni(ZdValue value)
    {
        if (value is not ZdValue.Map root)
            throw new ArgumentException("期望 zd Map（段 → 键值 Map）");
        var sb = new StringBuilder();
        // 全局段（""）先写
        if (root.Entries.TryGetValue("", out ZdValue? global))
            WriteSection(sb, "", global);
        foreach (var kv in root.Entries)
        {
            if (kv.Key.Length == 0)
                continue;
            sb.Append('[').Append(kv.Key).Append("]\n");
            WriteSection(sb, kv.Key, kv.Value);
        }
        return sb.ToString();
    }

    private static void WriteSection(StringBuilder sb, string sectionName, ZdValue sectionValue)
    {
        if (sectionValue is not ZdValue.Map map)
            return;
        foreach (var item in map.Entries)
        {
            sb.Append(item.Key).Append('=').Append(ValueText(item.Value, item.Key)).Append('\n');
        }
        if (sectionName.Length == 0 && map.Entries.Count > 0)
            sb.Append('\n');
    }

    private static string ValueText(ZdValue v, string key)
        => v switch
        {
            ZdValue.String s => s.Value,
            ZdValue.Integer i => i.Value.ToString(),
            ZdValue.Float f => f.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            ZdValue.Bool b => b.Value ? "true" : "false",
            _ => throw new ArgumentException($"INI 值 {key} 不支持 {v?.GetType().Name ?? "null"}"),
        };
}