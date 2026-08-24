using System.Collections;
using System.Globalization;
using System.Reflection;

namespace DoNetZD;

/// <summary>
/// 指定成员在 zd 中的键名（默认用成员名）。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ZdNameAttribute : Attribute
{
    /// <summary>zd map 中的键名。</summary>
    public string Name { get; }
    public ZdNameAttribute(string name) => Name = name ?? throw new ArgumentNullException(nameof(name));
}

/// <summary>标记成员不参与 zd 序列化/反序列化。</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ZdIgnoreAttribute : Attribute { }

/// <summary>
/// POCO ↔ <see cref="ZdValue"/> 反射绑定门面（零依赖）。
/// <para>序列化：扫描 public 字段 + 可读属性，尊重 <see cref="ZdNameAttribute"/>/<see cref="ZdIgnoreAttribute"/>；
/// 枚举→Integer；嵌套 POCO/容器递归。</para>
/// <para>反序列化：按无参构造创建实例，逐成员赋值（可写属性/非 initonly 字段）。</para>
/// <para>底层绑定器 <c>ZdPoco</c> 供 <see cref="ZdValue.FromObject"/>/<see cref="ZdValue.ToObject(Type)"/> 复用。</para>
/// </summary>
public static class ZdSerializer
{
    /// <summary>T 实例 → zd 字节。</summary>
    public static byte[] Serialize<T>(T obj) => ZdCodec.Encode(ZdValue.FromObject(obj));

    /// <summary>zd 字节 → T 实例。</summary>
    public static T Deserialize<T>(byte[] bytes) =>
        (T)ZdCodec.Decode(bytes).ToObject(typeof(T))!;
}

/// <summary>POCO ↔ ZdValue 的反射实现（内部）。供 ZdValue 复用，避免外部直接调用。</summary>
internal static class ZdPoco
{
    // ==================== POCO / 枚举 → ZdValue ====================

    /// <summary>
    /// 把 POCO 或枚举映射为 ZdValue。
    /// 仅在 <see cref="ZdValue.FromObject"/> 的 default 分支调用（即非基元/非已知容器）。
    /// </summary>
    public static ZdValue FromPoco(object obj)
    {
        if (obj is null)
            return ZdValue.Null.Instance;

        Type t = obj.GetType();
        if (t.IsEnum)
            return new ZdValue.Integer(Convert.ToInt64(obj, CultureInfo.InvariantCulture));

        var map = new Dictionary<string, ZdValue>();
        foreach (Member m in Members(t))
        {
            if (m.IsIgnored || !m.CanRead)
                continue;
            object? val = m.GetValue(obj);
            map[m.ZdName ?? m.Name] = ZdValue.FromObject(val);
        }
        return new ZdValue.Map(map);
    }

    // ==================== ZdValue → CLR ====================

    /// <summary>把 ZdValue 转换为指定类型的 CLR 实例。</summary>
    public static object? ToClr(ZdValue v, Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (v is ZdValue.Null) return type.IsValueType ? null : null;

        Type? underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
        {
            if (v is ZdValue.Null) return null;
            return ToClr(v, underlying);
        }

        if (type.IsEnum)
        {
            if (v is ZdValue.Integer ei) return Enum.ToObject(type, ei.Value);
            if (v is ZdValue.String es) return Enum.Parse(type, es.Value, ignoreCase: true);
            throw new InvalidCastException($"枚举 {type.Name} 期望 Integer/String，实际 {v.GetType().Name}");
        }

        switch (v)
        {
            case ZdValue.Integer i:
                if (type == typeof(long)) return i.Value;
                if (type == typeof(ulong)) return unchecked((ulong)i.Value);
                return Convert.ChangeType(i.Value, type, CultureInfo.InvariantCulture);
            case ZdValue.Float f:
                if (type == typeof(double)) return f.Value;
                if (type == typeof(float)) return (float)f.Value;
                return Convert.ChangeType(f.Value, type, CultureInfo.InvariantCulture);
            case ZdValue.Bool b:
                if (type == typeof(bool)) return b.Value;
                return Convert.ChangeType(b.Value, type, CultureInfo.InvariantCulture);
            case ZdValue.String s:
                if (type == typeof(string)) return s.Value;
                if (type == typeof(char)) return s.Value.Length > 0 ? s.Value[0] : '\0';
                return Convert.ChangeType(s.Value, type, CultureInfo.InvariantCulture);
            case ZdValue.Char c:
                if (type == typeof(char)) return (char)c.Codepoint;
                if (type == typeof(string)) return char.ConvertFromUtf32(c.Codepoint);
                return Convert.ChangeType(char.ConvertFromUtf32(c.Codepoint), type, CultureInfo.InvariantCulture);
            case ZdValue.Trit tr:
                if (type == typeof(long)) return tr.Value;
                return Convert.ChangeType(tr.Value, type, CultureInfo.InvariantCulture);
        }

        // 列表 / 数组
        if (typeof(IList).IsAssignableFrom(type) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            return ToList(v, type);

        // Dictionary<string, V>
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            && type.GenericTypeArguments[0] == typeof(string))
            return ToDict(v, type);

        // POCO
        if (v is ZdValue.Map pm)
        {
            object? instance = Activator.CreateInstance(type);
            if (instance is null)
                throw new InvalidOperationException($"无法创建 {type.FullName}（需要无参公共构造）");
            foreach (Member m in Members(type))
            {
                if (m.IsIgnored || !m.CanWrite)
                    continue;
                string key = m.ZdName ?? m.Name;
                if (!pm.Entries.TryGetValue(key, out ZdValue? fv))
                    continue;
                m.SetValue(instance, ToClr(fv, m.Type));
            }
            return instance;
        }

        throw new InvalidCastException($"无法把 {v.GetType().Name} 转为 {type.FullName}");
    }

    /// <summary>无具体类型（object 元素）时，转成自然 CLR 值。</summary>
    public static object? ToNatural(ZdValue v)
    {
        switch (v)
        {
            case ZdValue.Integer i: return i.Value;
            case ZdValue.Float f: return f.Value;
            case ZdValue.Bool b: return b.Value;
            case ZdValue.String s: return s.Value;
            case ZdValue.Char c: return char.ConvertFromUtf32(c.Codepoint);
            case ZdValue.Trit t: return t.Value;
            case ZdValue.Null: return null;
            case ZdValue.Array a:
                var lo = new List<object?>(a.Items.Count);
                for (int i = 0; i < a.Items.Count; i++) lo.Add(ToNatural(a.Items[i]));
                return lo;
            case ZdValue.Map m:
                var d = new Dictionary<string, object?>(m.Entries.Count);
                foreach (var kv in m.Entries) d[kv.Key] = ToNatural(kv.Value);
                return d;
            default: return v;
        }
    }

    private static object ToList(ZdValue v, Type type)
    {
        if (v is not ZdValue.Array arr)
            throw new InvalidCastException($"目标 {type.Name} 是列表，但 zd 值为 {v.GetType().Name}");

        Type? elem = type.IsArray ? type.GetElementType() : type.GenericTypeArguments.Length > 0 ? type.GenericTypeArguments[0] : null;
        bool isObjectElem = elem is null || elem == typeof(object);

        if (type.IsArray)
        {
            var array = Array.CreateInstance(elem!, arr.Items.Count);
            for (int i = 0; i < arr.Items.Count; i++)
                array.SetValue(isObjectElem ? ToNatural(arr.Items[i]) : ToClr(arr.Items[i], elem!), i);
            return array;
        }
        else
        {
            var list = (IList)Activator.CreateInstance(type)!;
            foreach (ZdValue item in arr.Items)
                list.Add(isObjectElem ? ToNatural(item) : ToClr(item, elem!));
            return list;
        }
    }

    private static object ToDict(ZdValue v, Type type)
    {
        if (v is not ZdValue.Map m)
            throw new InvalidCastException($"目标 {type.Name} 是字典，但 zd 值为 {v.GetType().Name}");
        Type vt = type.GenericTypeArguments[1];
        bool isObjectV = vt == typeof(object);
        var dict = (IDictionary)Activator.CreateInstance(type)!;
        foreach (var kv in m.Entries)
            dict[kv.Key] = isObjectV ? ToNatural(kv.Value) : ToClr(kv.Value, vt);
        return dict;
    }

    // ==================== 成员反射（按类型缓存）====================

    private static readonly Dictionary<Type, List<Member>> _cache = new();

    private static List<Member> Members(Type t)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(t, out var cached))
                return cached;
            var list = new List<Member>();
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsLiteral) continue;  // const
                var nameAttr = f.GetCustomAttribute<ZdNameAttribute>();
                var ignore = f.GetCustomAttribute<ZdIgnoreAttribute>();
                list.Add(new Member
                {
                    Name = f.Name,
                    ZdName = nameAttr?.Name,
                    IsIgnored = ignore != null,
                    Type = f.FieldType,
                    CanRead = true,
                    CanWrite = !f.IsInitOnly,
                    GetValue = o => f.GetValue(o),
                    SetValue = (o, val) => f.SetValue(o, val),
                });
            }
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead) continue;
                var nameAttr = p.GetCustomAttribute<ZdNameAttribute>();
                var ignore = p.GetCustomAttribute<ZdIgnoreAttribute>();
                list.Add(new Member
                {
                    Name = p.Name,
                    ZdName = nameAttr?.Name,
                    IsIgnored = ignore != null,
                    Type = p.PropertyType,
                    CanRead = p.CanRead,
                    CanWrite = p.CanWrite,
                    GetValue = o => p.GetValue(o),
                    SetValue = p.CanWrite ? (Action<object, object?>)((o, val) => p.SetValue(o, val)) : (_, _) => { },
                });
            }
            _cache[t] = list;
            return list;
        }
    }

    private sealed class Member
    {
        public string Name = "";
        public string? ZdName;
        public bool IsIgnored;
        public Type Type = null!;
        public bool CanRead;
        public bool CanWrite;
        public Func<object, object?> GetValue = null!;
        public Action<object, object?> SetValue = null!;
    }
}
