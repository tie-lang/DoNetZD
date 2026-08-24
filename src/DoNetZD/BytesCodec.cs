namespace DoNetZD;

/// <summary>
/// 字节 / base64 ↔ ZdValue 互转。
/// 约定：外部字节序列以 zd 数组（每位一个 Integer 0..255）表示。
/// </summary>
public static class BytesCodec
{
    /// <summary>byte[] → zd 数组（每位 Integer 0..255）。</summary>
    public static ZdValue FromBytes(byte[] bytes)
    {
        if (bytes is null)
            throw new ArgumentNullException(nameof(bytes));
        var items = new ZdValue[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            items[i] = new ZdValue.Integer(bytes[i]);
        return new ZdValue.Array(items);
    }

    /// <summary>zd 数组 → byte[]。要求数组元素均为 0..255 的 Integer。</summary>
    public static byte[] ToBytes(ZdValue value)
    {
        if (value is not ZdValue.Array arr)
            throw new ArgumentException("期望 zd 数组，实际 " + (value?.GetType().Name ?? "null"));
        var bytes = new byte[arr.Items.Count];
        for (int i = 0; i < arr.Items.Count; i++)
        {
            if (arr.Items[i] is not ZdValue.Integer n || n.Value < 0 || n.Value > 255)
                throw new ArgumentException($"位置 {i} 不是 0..255 整数，不能转字节");
            bytes[i] = (byte)n.Value;
        }
        return bytes;
    }

    /// <summary>base64 字符串 → zd 数组。</summary>
    public static ZdValue FromBase64(string base64)
        => FromBytes(Convert.FromBase64String(base64));

    /// <summary>zd 数组 → base64 字符串。</summary>
    public static string ToBase64(ZdValue value)
        => Convert.ToBase64String(ToBytes(value));
}