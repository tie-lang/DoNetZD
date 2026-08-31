using System.Collections.Generic;

namespace DoNetZD;

/// <summary>
/// 列式容器的一列：列名 + 列类型 + 该列全部值（值须为同构的 <see cref="ZdValue"/>）。
/// 供表格型数据按列存储，去重 + 局部性 + 体积可预测。
/// </summary>
public sealed class Column
{
    /// <summary>列名。</summary>
    public string Name { get; }
    /// <summary>列类型（如 "string"/"int"/"double" 等，语言无关的字符串描述）。</summary>
    public string Type { get; }
    /// <summary>该列的全部值。</summary>
    public IReadOnlyList<ZdValue> Values { get; }

    public Column(string name, string type, IReadOnlyList<ZdValue> values)
    {
        Name = name ?? "";
        Type = type ?? "";
        Values = values ?? System.Array.Empty<ZdValue>();
    }
}

/// <summary>
/// 列式容器（zd v2）。字节格式（tag 0xd6）：
/// <c>0xd6 + encode_i64(列数) + 每列[列名(str) + 列类型(str) + encode_i64(列长)] + 各列值</c>。
/// 值按列分组依次存放，每列值长度由描述段的列长给出（值本身仍自描述）。
/// </summary>
public static class ZdColumnar
{
    /// <summary>便捷构造一列，值来自可变参数。</summary>
    public static Column Col(string name, string type, params ZdValue[] values)
        => new(name, type, values);

    /// <summary>便捷构建列式容器值。</summary>
    public static ZdValue.Columnar Make(params Column[] columns) => new(columns);
}