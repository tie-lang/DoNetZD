namespace DoNetZD;

/// <summary>
/// zd 解码失败异常（标签不识别 / 字节不足 / 结构非法）。
/// 对应 tie 侧解码'位置哨兵'语义；.NET 侧在类型化解析路径以异常形式暴露。
/// </summary>
public sealed class ZdFormatException : Exception
{
    /// <summary>发生异常时的字节位置。</summary>
    public int Position { get; }

    public ZdFormatException(string message, int position, Exception? inner = null)
        : base($"{message}（位置 {position}）", inner)
        => Position = position;
}

/// <summary>
/// tie:zd 类型化模型基类。
/// 一类一型：整数/浮点/布尔/字符/三值/字符串/数组/map。编码时按类型映射到对应 zd 标签。
/// </summary>
public abstract class ZdValue
{
    private ZdValue() { }

    /// <summary>整数（tie i64）。</summary>
    public sealed class Integer(long value) : ZdValue { public long Value { get; } = value; }

    /// <summary>浮点（double，覆盖 f32/f64；f32 解码为 float 后提升为 double）。</summary>
    public sealed class Float(double value) : ZdValue { public double Value { get; } = value; }

    /// <summary>布尔。</summary>
    public sealed class Bool(bool value) : ZdValue { public bool Value { get; } = value; }

    /// <summary>字符（Unicode 码点，tie char）。</summary>
    public sealed class Char(int codepoint) : ZdValue { public int Codepoint { get; } = codepoint; }

    /// <summary>三值（tie trit，-1/0/1）。</summary>
    public sealed class Trit(long value) : ZdValue { public long Value { get; } = value; }

    /// <summary>字符串。</summary>
    public sealed class String(string value) : ZdValue { public string Value { get; } = value; }

    /// <summary>null 哨兵：用于 JSON/XML 等含 null 的外部格式中转。
    /// 注意 zd 字节格式本身没有 null 标签——Encode 到字节会抛异常，但可输出为 JSON null 等。</summary>
    public sealed class Null : ZdValue
    {
        /// <summary>单例。</summary>
        public static readonly Null Instance = new();

        private Null() { }
    }

    /// <summary>数组（长度在头部，元素顺序排列）。</summary>
    public sealed class Array(IReadOnlyList<ZdValue> items) : ZdValue { public IReadOnlyList<ZdValue> Items { get; } = items; }

    /// <summary>map（键为字符串，值可为任意 zd 值）。</summary>
    public sealed class Map(IReadOnlyDictionary<string, ZdValue> entries) : ZdValue
    {
        public IReadOnlyDictionary<string, ZdValue> Entries { get; } = entries;
    }

    // ==================== 便捷构造 ====================

    public static Integer I(long v) => new Integer(v);
    public static Float F(double v) => new Float(v);
    public static Bool B(bool v) => new Bool(v);
    public static String S(string v) => new String(v);
    public static Trit T(long v) => new Trit(v);

    // ==================== CLR 对象 ⇒ zd 值（供编码便捷入口）====================

    /// <summary>
    /// 把 CLR 对象映射为 zd 值：整数族（含 ulong）→ Integer；double/float → Float；
    /// bool → Bool；string → String；IList → Array；IDictionary&lt;string,?&gt; → Map；
    /// 其它可实现 MakeZd() 自定义；null 不支持（抛异常）。
    /// </summary>
    public static ZdValue FromObject(object? obj)
    {
        switch (obj)
        {
            case null: return Null.Instance;                                   // null → 哨兵（zd 字节不可编码）
            case Integer zd: return zd;
            case Float zd: return zd;
            case Bool zd: return zd;
            case String zd: return zd;
            case Trit zd: return zd;
            case Char zd: return zd;
            case Array zd: return zd;
            case Map zd: return zd;
            case string s: return new String(s);
            case char ch: return new Char(char.ConvertToUtf32(ch.ToString(), 0));
            case bool b: return new Bool(b);
            case double d: return new Float(d);
            case float f: return new Float(f);
            case ulong ul: return new Integer(unchecked((long)ul));
            case long l: return new Integer(l);
            case int i: return new Integer(i);
            case short sh: return new Integer(sh);
            case byte by: return new Integer(by);
            case uint ui: return new Integer(ui);
            case ushort us: return new Integer(us);
            case sbyte sb: return new Integer(sb);
            case IReadOnlyList<ZdValue> zlist: return new Array(zlist);
            case System.Collections.IList list: return new Array(list.Cast<object?>().Select(FromObject).ToList());
            case IReadOnlyDictionary<string, object?> zmap:
                return new Map(zmap.ToDictionary(kv => kv.Key, kv => FromObject(kv.Value)));
            case IDictionary<string, object?> omap:
                return new Map(omap.ToDictionary(kv => kv.Key, kv => FromObject(kv.Value)));
            case System.Collections.IDictionary raw:
                return new Map(raw.Keys.Cast<string>().ToDictionary(
                    k => k, k => FromObject(raw[k])));
            default:
                throw new ArgumentException($"不支持的类型 {obj.GetType().FullName}；请手动构造 ZdValue", nameof(obj));
        }
    }

    /// <summary>打印直观表示（调试/日志用）。</summary>
    public override string ToString()
    {
        switch (this)
        {
            case Integer i: return i.Value.ToString();
            case Float f: return f.Value.ToString("R");
            case Bool b: return b.Value ? "true" : "false";
            case Char c: return $"@{char.ConvertFromUtf32(c.Codepoint)} (U+{c.Codepoint:X4})";
            case Trit t: return t.Value.ToString();
            case String s: return "\"" + s.Value + "\"";
            case Null: return "null";
            case Array a: return "[" + string.Join(", ", a.Items) + "]";
            case Map m: return "{" + string.Join(", ", m.Entries.Select(kv => $"{kv.Key}: {kv.Value}")) + "}";
            default: return base.ToString() ?? "";
        }
    }
}