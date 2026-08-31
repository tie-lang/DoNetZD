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

    // ---- YAML ----
    /// <summary>YAML 文本 → ZdValue。</summary>
    public static ZdValue YamlToValue(string yaml) => YamlCodec.FromYaml(yaml);
    /// <summary>ZdValue → YAML 文本（块式）。</summary>
    public static string ValueToYaml(ZdValue value) => YamlCodec.ToYaml(value);

    // ---- TOML ----
    /// <summary>TOML 文本 → ZdValue（根 Map）。</summary>
    public static ZdValue TomlToValue(string toml) => TomlCodec.FromToml(toml);
    /// <summary>ZdValue → TOML 文本。</summary>
    public static string ValueToToml(ZdValue value) => TomlCodec.ToToml(value);

    // ---- 便捷：任意格式 → zd 字节，zd 字节 → 任意格式 ----
    /// <summary>JSON 文本 → zd 字节（经 ZdValue 中转；含 null 时抛异常）。</summary>
    public static byte[] JsonToBytes(string json) => ZdCodec.Encode(JsonCodec.Parse(json));
    /// <summary>zd 字节 → JSON 文本。</summary>
    public static string BytesToJson(byte[] zdBytes) => JsonCodec.Serialize(ZdCodec.Decode(zdBytes));

    // ---- 调试 / 字节可视化 ----
    /// <summary>把 zd 字节（可带魔数头）转储为“偏移 + 类型注解”的可读文本。</summary>
    public static string Dump(byte[] zdBytes) => ZdDump.Dump(zdBytes);

    // ---- v2 容器 / 字符串字典 / 列式 ----
    /// <summary>ZdValue → v2 完整容器字节（含 10 字节头；可按 <paramref name="options"/> 附着 schema/哈希/字典）。</summary>
    public static byte[] ValueToV2(ZdValue value, ZdV2Options? options = null) => ZdV2.Encode(value, options);
    /// <summary>v2 完整容器字节 → ZdValue（校验哈希）。</summary>
    public static ZdValue V2ToValue(byte[] v2Bytes) => ZdV2.Decode(v2Bytes);
    /// <summary>ZdValue → 带字符串字典引用的字节（[池段][正文]）。</summary>
    public static byte[] ValueToDictBytes(ZdValue value) => ZdCodec.EncodeWithPool(value);
    /// <summary>带字符串字典引用的字节 → ZdValue。</summary>
    public static ZdValue DictBytesToValue(byte[] bytes) => ZdCodec.DecodeWithPool(bytes);
}