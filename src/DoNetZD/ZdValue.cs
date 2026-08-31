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

    /// <summary>null 值（v2）：0xc0 可编码，区分「缺失」与「空值」；亦用于 JSON/XML 中转。</summary>
    public sealed class Null : ZdValue
    {
        /// <summary>单例。</summary>
        public static readonly Null Instance = new();

        private Null() { }
    }

    /// <summary>bytes（v2）：原始二进制 blob，内容为 byte[]。</summary>
    public sealed class Bytes(byte[] content) : ZdValue { public byte[] Content { get; } = content; }

    /// <summary>ext 扩展类型（v2）：i64 类型标记 + 原始载荷；tie-IR/char/trit/平台数据走此。</summary>
    public sealed class Ext(long typeTag, byte[] payload) : ZdValue
    {
        public long TypeTag { get; } = typeTag;
        public byte[] Payload { get; } = payload;
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
            case Bytes zd: return zd;
            case Ext zd: return zd;
            case Array zd: return zd;
            case Map zd: return zd;
            case string s: return new String(s);
            case byte[] raw: return new Bytes(raw);
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
                // POCO / 枚举 → 反射绑定（见 ZdPoco）
                return ZdPoco.FromPoco(obj);
        }
    }

    // ==================== 深度比较 / 哈希 / 合并 / 遍历 ====================

    /// <summary>递归比较两个 zd 值（类型与值都要一致）。</summary>
    public bool DeepEquals(ZdValue? other) => DeepEquals(this, other);

    private static bool DeepEquals(ZdValue? a, ZdValue? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        switch (a)
        {
            case Integer ia when b is Integer ib: return ia.Value == ib.Value;
            case Float fa when b is Float fb: return fa.Value == fb.Value;
            case Bool ba when b is Bool bb: return ba.Value == bb.Value;
            case Char ca when b is Char cb: return ca.Codepoint == cb.Codepoint;
            case Trit ta when b is Trit tb: return ta.Value == tb.Value;
            case String sa when b is String sb: return sa.Value == sb.Value;
            case Bytes ba when b is Bytes bb: return ContentsEqual(ba.Content, bb.Content);
            case Ext ea when b is Ext eb: return ea.TypeTag == eb.TypeTag && ContentsEqual(ea.Payload, eb.Payload);
            case Null when b is Null: return true;
            case Array aa when b is Array ab:
                if (aa.Items.Count != ab.Items.Count) return false;
                for (int i = 0; i < aa.Items.Count; i++)
                    if (!DeepEquals(aa.Items[i], ab.Items[i])) return false;
                return true;
            case Map ma when b is Map mb:
                if (ma.Entries.Count != mb.Entries.Count) return false;
                foreach (var kv in ma.Entries)
                {
                    if (!mb.Entries.TryGetValue(kv.Key, out var bv)) return false;
                    if (!DeepEquals(kv.Value, bv)) return false;
                }
                return true;
            default: return false;
        }
    }

    /// <summary>深度哈希（结合类型标签与值；容器递归组合元素哈希）。</summary>
    public override int GetHashCode()
    {
        switch (this)
        {
            case Integer i: return Combine(1, i.Value.GetHashCode());
            case Float f: return Combine(2, f.Value.GetHashCode());
            case Bool b: return Combine(3, b.Value.GetHashCode());
            case Char c: return Combine(4, c.Codepoint.GetHashCode());
            case Trit t: return Combine(5, t.Value.GetHashCode());
            case String s: return Combine(6, s.Value.GetHashCode());
            case Null: return 7;
            case Bytes by: return Combine(10, HashBytes(by.Content));
            case Ext ex: return Combine(Combine(11, ex.TypeTag.GetHashCode()), HashBytes(ex.Payload));
            case Array a:
                int ah = 8;
                foreach (var x in a.Items) ah = Combine(ah, x.GetHashCode());
                return ah;
            case Map m:
                int mh = 9;
                foreach (var kv in m.Entries)
                    mh = Combine(Combine(mh, kv.Key.GetHashCode()), kv.Value.GetHashCode());
                return mh;
            default: return base.GetHashCode();
        }
    }

    /// <summary>
    /// RFC 7396 风格合并补丁：返回合并后的新值（不修改本值）。
    /// 规则：补丁为 Map 时按键合并——值为 Null 则删键；值与目标同为 Map 则递归；否则替换；
    /// 补丁非 Map 时整体替换。常用于配置增量更新。
    /// </summary>
    public ZdValue Merge(ZdValue patch)
    {
        if (patch is null) throw new ArgumentNullException(nameof(patch));
        if (patch is Map pm && this is Map tm)
        {
            var result = new Dictionary<string, ZdValue>();
            foreach (var kv in tm.Entries)
                result[kv.Key] = kv.Value;
            foreach (var kv in pm.Entries)
            {
                if (kv.Value is Null)
                {
                    result.Remove(kv.Key);
                }
                else if (result.TryGetValue(kv.Key, out var existing) && existing is Map em && kv.Value is Map mm)
                {
                    result[kv.Key] = em.Merge(mm);
                }
                else
                {
                    result[kv.Key] = kv.Value;
                }
            }
            return new Map(result);
        }
        return patch;  // 补丁非 Map → 整体替换
    }

    /// <summary>先序遍历整棵值树（先回调本值，再下钻 Array/Map 子项）。</summary>
    public void Visit(Action<ZdValue> action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        VisitCore(action);
    }

    private void VisitCore(Action<ZdValue> a)
    {
        a(this);
        switch (this)
        {
            case Array arr:
                for (int i = 0; i < arr.Items.Count; i++) arr.Items[i].VisitCore(a);
                break;
            case Map m:
                foreach (var kv in m.Entries) kv.Value.VisitCore(a);
                break;
        }
    }

    // ==================== 转为 CLR 类型（POCO 反序列化入口）====================

    /// <summary>把本值转为指定 CLR 类型（基元/列表/字典/POCO）。委托 ZdPoco。</summary>
    public object? ToObject(Type type) => ZdPoco.ToClr(this, type);

    /// <summary>把本值转为 T（基元/列表/字典/POCO）。</summary>
    public T? ToObject<T>() => (T?)ToObject(typeof(T));

    private static int Combine(int a, int b) => unchecked((a * 31) + b);

    private static bool ContentsEqual(byte[] a, byte[] b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static int HashBytes(byte[] data)
    {
        if (data is null || data.Length == 0) return 0;
        unchecked
        {
            int h = 17;
            for (int i = 0; i < data.Length; i++)
                h = (h * 31) + data[i];
            return h;
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
            case Bytes by: return "<bytes len=" + by.Content.Length + ">";
            case Ext ex: return $"<ext#{ex.TypeTag} len={ex.Payload.Length}>";
            case Array a: return "[" + string.Join(", ", a.Items) + "]";
            case Map m: return "{" + string.Join(", ", m.Entries.Select(kv => $"{kv.Key}: {kv.Value}")) + "}";
            default: return base.ToString() ?? "";
        }
    }
}