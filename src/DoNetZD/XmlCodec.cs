using System.Linq;
using System.Xml;

namespace DoNetZD;

/// <summary>
/// XML ↔ ZdValue 互转（System.Xml）。
/// 映射约定（对齐常见 XML→JSON 惯例）：
///   * 元素仅有文本（无属性无子元素）→ String(文本，trim)
///   * 元素有属性/子元素 → Map：属性键 "@名"；子元素按名分组，重复→Array；
///     元素自身的文本内容（有子元素时）→ 键 "#text"
///   * 空元素 → String("")
/// ToXml 生成镜像元素；Map 的 "@名"→属性、"#text"→文本、其余键→子元素、值 Array→同名重复子元素。
/// </summary>
public static class XmlCodec
{
    /// <summary>XML 文本 → zd 值（取根元素）。</summary>
    public static ZdValue FromXml(string xml)
    {
        if (xml is null)
            throw new ArgumentNullException(nameof(xml));
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        XmlElement? root = doc.DocumentElement;
        if (root is null)
            throw new FormatException("XML 无根元素");
        return Element(root);
    }

    private static ZdValue Element(XmlElement el)
    {
        var elementList = el.ChildNodes.Cast<XmlNode>().Where(n => n.NodeType == XmlNodeType.Element).ToList();

        if (elementList.Count == 0)
        {
            // 叶子：仅属性/仅文本
            if (el.Attributes.Count == 0)
                return new ZdValue.String(el.InnerText.Trim());
            var attrs = new Dictionary<string, ZdValue>();
            foreach (XmlAttribute a in el.Attributes)
                attrs["@" + a.Name] = new ZdValue.String(a.Value);
            string text = el.InnerText.Trim();
            if (text.Length > 0)
                attrs["#text"] = new ZdValue.String(text);
            return new ZdValue.Map(attrs);
        }

        var entries = new Dictionary<string, ZdValue>();
        foreach (XmlAttribute a in el.Attributes)
            entries["@" + a.Name] = new ZdValue.String(a.Value);

        // 子元素按名分组
        var byName = elementList.GroupBy(e => e.Name)
                                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (var g in byName)
        {
            if (g.Value.Count == 1)
                entries[g.Key] = Element((XmlElement)g.Value[0]);
            else
                entries[g.Key] = new ZdValue.Array(g.Value.Select(e => Element((XmlElement)e)).ToList());
        }
        return new ZdValue.Map(entries);
    }

    /// <summary>zd 值 → XML 文本（rootName 为根元素名）。</summary>
    public static string ToXml(ZdValue value, string rootName)
    {
        if (string.IsNullOrWhiteSpace(rootName))
            throw new ArgumentException("rootName 不能为空");
        var doc = new XmlDocument();
        XmlElement root = doc.CreateElement(rootName);
        Fill(doc, root, value);
        doc.AppendChild(root);
        return doc.OuterXml;
    }

    private static void Fill(XmlDocument doc, XmlElement el, ZdValue value)
    {
        switch (value)
        {
            case ZdValue.Map map:
                string text = null;
                foreach (var kv in map.Entries)
                {
                    if (kv.Key.StartsWith("@"))
                    {
                        el.SetAttribute(kv.Key.Substring(1), ScalarText(kv.Value));
                    }
                    else if (kv.Key == "#text")
                    {
                        text = ScalarText(kv.Value);
                    }
                    else if (kv.Value is ZdValue.Array arr)
                    {
                        foreach (ZdValue item in arr.Items)
                        {
                            XmlElement child = doc.CreateElement(kv.Key);
                            Fill(doc, child, item);
                            el.AppendChild(child);
                        }
                    }
                    else
                    {
                        XmlElement child = doc.CreateElement(kv.Key);
                        Fill(doc, child, kv.Value);
                        el.AppendChild(child);
                    }
                }
                if (text != null)
                    el.InnerText = text;
                break;
            case ZdValue.Array:
                // 顶层数组无处安放：无操作
                break;
            default:
                el.InnerText = ScalarText(value);
                break;
        }
    }

    private static string ScalarText(ZdValue v) => v switch
    {
        ZdValue.String s => s.Value,
        ZdValue.Integer i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ZdValue.Float f => f.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        ZdValue.Bool b => b.Value ? "true" : "false",
        ZdValue.Char c => char.ConvertFromUtf32(c.Codepoint),
        ZdValue.Null => "",
        ZdValue.Trit t => t.Value.ToString(),
        _ => throw new ArgumentException($"标量文本不支持 {v?.GetType().Name ?? "null"}"),
    };
}