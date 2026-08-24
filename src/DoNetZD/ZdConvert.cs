namespace DoNetZD;

/// <summary>
/// DoNetZD 互转门面：zd ↔ 常见格式。所有格式都以 <see cref="ZdValue"/> 为统一中转模型：
/// 先 Parse 成 ZdValue（或由 ZdCodec.Encode 得到 zd 字节），再从 ZdValue 序列化回目标格式。
/// 覆盖：JSON / tie:data / 字节 / base64 / CSV / INI / XML。
/// </summary>
public static class ZdConvert
{
    // ---- JSON ----
    /// <summary>JSON 文本 → ZdValue。</summary>
    public static ZdValue JsonToValue(string json) => JsonCodec.Parse(json);
    /// <summary>ZdValue → JSON 文本。</summary>
    public static string ValueToJson(ZdValue value) => JsonCodec.Serialize(value);

    // ---- tie:data（JSON 子集，格式别名）----
    /// <summary>tie:data 文本 → ZdValue（等价 JSON 解析）。</summary>
    public static ZdValue TieDataToValue(string data) => JsonCodec.Parse(data);
    /// <summary>ZdValue → tie:data 文本（可读 JSON 风格）。</summary>
    public static string ValueToTieData(ZdValue value) => JsonCodec.Serialize(value, pretty: true);

    // ---- 字节 / base64 ----
    /// <summary>byte[] → ZdValue（数组，每位 0..255）。</summary>
    public static ZdValue BytesToValue(byte[] bytes) => BytesCodec.FromBytes(bytes);
    /// <summary>ZdValue（数组）→ byte[]。</summary>
    public static byte[] ValueToBytes(ZdValue value) => BytesCodec.ToBytes(value);
    /// <summary>base64 → ZdValue。</summary>
    public static ZdValue Base64ToValue(string b64) => BytesCodec.FromBase64(b64);
    /// <summary>ZdValue（数组）→ base64。</summary>
    public static string ValueToBase64(ZdValue value) => BytesCodec.ToBase64(value);

    // ---- CSV ----
    /// <summary>CSV 文本 → ZdValue（二维表，行/列数组，格为 String）。</summary>
    public static ZdValue CsvToValue(string csv) => CsvCodec.FromCsv(csv);
    /// <summary>ZdValue（二维表）→ CSV 文本。</summary>
    public static string ValueToCsv(ZdValue value) => CsvCodec.ToCsv(value);

    // ---- INI ----
    /// <summary>INI 文本 → ZdValue（段 → 键值 Map；全局段 key=""）。</summary>
    public static ZdValue IniToValue(string ini) => IniCodec.FromIni(ini);
    /// <summary>ZdValue（段 Map）→ INI 文本。</summary>
    public static string ValueToIni(ZdValue value) => IniCodec.ToIni(value);

    // ---- XML ----
    /// <summary>XML 文本 → ZdValue。</summary>
    public static ZdValue XmlToValue(string xml) => XmlCodec.FromXml(xml);
    /// <summary>ZdValue → XML 文本（rootName 为根元素名）。</summary>
    public static string ValueToXml(ZdValue value, string rootName) => XmlCodec.ToXml(value, rootName);

    // ---- 便捷：任意格式 → zd 字节，zd 字节 → 任意格式 ----
    /// <summary>JSON 文本 → zd 字节（经 ZdValue 中转；含 null 时抛异常）。</summary>
    public static byte[] JsonToBytes(string json) => ZdCodec.Encode(JsonCodec.Parse(json));
    /// <summary>zd 字节 → JSON 文本。</summary>
    public static string BytesToJson(byte[] zdBytes) => JsonCodec.Serialize(ZdCodec.Decode(zdBytes));
}