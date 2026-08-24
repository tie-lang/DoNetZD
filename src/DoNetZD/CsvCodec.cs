using System.Text;

namespace DoNetZD;

/// <summary>
/// CSV ↔ ZdValue 互转。
/// 约定：表格 → zd 数组（每行是一个数组，每格为 String）。RFC 4180 风格解析
/// （逗号分隔、双引号包裹、" 转义、换行分列）。值在转出时统一转为字符串。
/// </summary>
public static class CsvCodec
{
    /// <summary>CSV 文本 → zd 二维数组（行 / 列，每格 String；跳过空尾行）。</summary>
    public static ZdValue FromCsv(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        var rows = new List<ZdValue>();
        // 以真正的行结束切分，同时兼容 CRLF / LF
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        foreach (string line in lines)
        {
            if (line.Length == 0 && rows.Count > 0)
                continue; // 末尾空行（中间空行保留为空行？简化：末尾跳过）
            rows.Add(new ZdValue.Array(ParseLine(line).Select(c => (ZdValue)new ZdValue.String(c)).ToList()));
        }
        if (rows.Count > 0)
            rows.RemoveAll(r => ((ZdValue.Array)r).Items.Count == 1 && ((ZdValue.String)((ZdValue.Array)r).Items[0]).Value.Length == 0);
        return new ZdValue.Array(rows);
    }

    private static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var cur = new StringBuilder();
        bool inQuotes = false;
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                cur.Append(c); i++;
            }
            else
            {
                if (c == '"') { inQuotes = true; i++; }
                else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); i++; }
                else { cur.Append(c); i++; }
            }
        }
        fields.Add(cur.ToString());
        return fields;
    }

    /// <summary>zd 二维数组 → CSV 文本。需数组内为数组，单元格为标量/字符串。</summary>
    public static string ToCsv(ZdValue value)
    {
        if (value is not ZdValue.Array rows)
            throw new ArgumentException("期望 zd 二维数组（行嵌套）");
        var sb = new StringBuilder();
        foreach (ZdValue row in rows.Items)
        {
            var cells = row is ZdValue.Array arr ? arr.Items.Select(CellText) : new[] { CellText(row) };
            sb.Append(string.Join(",", cells.Select(Escape)));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string CellText(ZdValue v) => v switch
    {
        ZdValue.String s => s.Value,
        ZdValue.Integer i => i.Value.ToString(),
        ZdValue.Float f => f.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        ZdValue.Bool b => b.Value ? "true" : "false",
        ZdValue.Char c => char.ConvertFromUtf32(c.Codepoint),
        _ => throw new ArgumentException($"单元格不支持 {v?.GetType().Name ?? "null"} 转文本"),
    };

    private static string Escape(string s)
        => s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}