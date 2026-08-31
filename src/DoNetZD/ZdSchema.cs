using System.Collections.Generic;

namespace DoNetZD;

/// <summary>
/// v2 Schema 段（自描述元数据）：字段名 + 类型 字符串数组。
/// 字节格式：<c>0xc7 + 数组头(字段数) + 每字段[字段名(str) + 类型(str)]</c>。
/// 供任意语言读到容器后可反序列化到对象/校验字段集合。
/// </summary>
public static class ZdSchema
{
    /// <summary>编码 schema 段（(字段名, 类型) 列表）。</summary>
    public static byte[] Encode(IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        var b = new ZdBuilder(32 + (fields?.Count ?? 0) * 16);
        b.AppendByte(Zd.TagSchema);
        b.AppendBytes(Zd.EncodeArrayHeader(fields?.Count ?? 0));
        if (fields != null)
            foreach (var f in fields)
            {
                b.AppendBytes(Zd.EncodeString(f.Key ?? ""));
                b.AppendBytes(Zd.EncodeString(f.Value ?? ""));
            }
        return b.ToArray();
    }

    /// <summary>解码 schema 段（pos 指向 0xc7，消费整段），返回 (字段名, 类型) 列表。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Decode(byte[] t, ref int pos)
    {
        int start = pos;
        if (t is null || pos >= t.Length || t[pos] != Zd.TagSchema)
            throw new ZdFormatException($"期望 schema 段(0xc7)，实际 0x{(t is null || pos >= t.Length ? "EOF" : t[pos].ToString("X2"))}", start);
        pos++;
        long count = Zd.ReadArrayLength(t, ref pos);
        if (count < 0 || count > int.MaxValue)
            throw new ZdFormatException("schema 字段数越界", start);
        var list = new List<KeyValuePair<string, string>>((int)count);
        for (int i = 0; i < count; i++)
        {
            ZdValue nameV = DecodeFieldString(t, ref pos, start);
            ZdValue typeV = DecodeFieldString(t, ref pos, start);
            list.Add(new KeyValuePair<string, string>(
                nameV is ZdValue.String ns ? ns.Value : "",
                typeV is ZdValue.String ts ? ts.Value : ""));
        }
        return list;
    }

    private static ZdValue DecodeFieldString(byte[] t, ref int pos, int start)
    {
        if (pos >= t.Length)
            throw new ZdFormatException("schema 字符串缺失", start);
        byte tag = t[pos];
        long len;
        if (tag >= 0xA0 && tag <= 0xBF) { pos++; len = tag - 0xA0; }
        else if (tag == Zd.TagStr8) { pos++; if (pos >= t.Length) throw new ZdFormatException("schema str8 长度缺失", start); len = t[pos++]; }
        else if (tag == Zd.TagStr16)
        {
            pos++;
            if (pos + 2 > t.Length) throw new ZdFormatException("schema str16 越界", start);
            len = (t[pos] << 8) | t[pos + 1]; pos += 2;
        }
        else if (tag == Zd.TagStr32)
        {
            pos++;
            if (pos + 4 > t.Length) throw new ZdFormatException("schema str32 越界", start);
            len = ((long)t[pos] << 24) | ((long)t[pos + 1] << 16) | ((long)t[pos + 2] << 8) | t[pos + 3]; pos += 4;
        }
        else
        {
            throw new ZdFormatException($"schema 字段期望字符串，实际 0x{tag:X2}", start);
        }
        if (len < 0 || len > int.MaxValue || pos + len > t.Length)
            throw new ZdFormatException("schema 字符串长度越界", start);
        var bytes = new byte[len];
        System.Array.Copy(t, pos, bytes, 0, (int)len);
        pos += (int)len;
        return new ZdValue.String(System.Text.Encoding.UTF8.GetString(bytes));
    }
}