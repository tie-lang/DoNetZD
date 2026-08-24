# DoNetZD（DNZD）

**用 C# 逐字节复刻 tie 语言 `tie:zd` 二进制序列化格式**的独立库，供 .NET 宿主与
tie 插件之间做跨语言结构化数据封包。

## 它能做什么

`tie:zd` 是 tie 工具链自带的二进制序列化格式（见 tie 仓库
`tieDB/persist/zd.tie`），MessagePack 风格（类型标签 + 紧凑编码）叠 Protobuf 风格
varint，能够把整数 / 浮点 / 布尔 / 字符 / 三值 / 字符串 / 数组 / map 紧凑地写成字节。

DoNetZD 用 .NET 忠实实现了同一格式，字节布局与 tie 侧**逐字节一致**：

- **原语层**：`Zd.EncodeI64 / EncodeF64 / EncodeString / EncodeArrayHeader …` 与
  `Zd.Save / Load / IsZd`（带 `"TIEDBZD"` 魔数头），适合需要字节级精确控制的场景。
- **类型化层**：`ZdValue` 模型 + `ZdCodec.Encode / Decode` 递归编解码，并支持直接把
  CLR 对象（`long/int/bool/string/List/Dictionary` 等）映射成 zd 值，开箱即用。

写任何需要与 tie 程序互换数据的 .NET 程序时，用同一个 zd 格式就能无缝对接。

## 特性

- **跨语言互通**：字节布局采样自 tie:zd 定稿，golden 字节向量测试作为互通护栏。
- **兼容老框架**：目标框架 `netstandard2.0`，可在 .NET Framework / .NET Core / .NET
  全系运行，零第三方依赖。
- **两套 API**：原语（贴近 tie `namespace zd`）+ 类型化模型（方便组装结构化数据）。
- **格式自描述**：每值带类型标签，无外部 schema，天然支持任意嵌套与异构容器。
- **流式 + 异步 IO**：`ZdStream` 直读写 `Stream`，`Zd.SaveAsync/LoadAsync` 异步落盘。
- **CRC32 完整性**：`ZdCrc32` 自实现 + `Zd.SaveChecked/LoadChecked` 防篡改文件。
- **POCO 绑定**：`ZdSerializer` + `[ZdName]`/`[ZdIgnore]` 反射映射，枚举/列表/字典/嵌套递归。
- **查询与变换**：`ZdPath` 路径访问、`DeepEquals`/`GetHashCode`、`Merge`（RFC 7396）、`Visit` 遍历。
- **字节可视化**：`ZdDump.Dump` 输出带偏移与类型注解的字节树。

## 使用示例

```csharp
using DoNetZD;

// 原语：手写字节精确控制
byte[] n = Zd.EncodeI64(300);          // 0xCD 0x01 0x2C（u16 大端，紧凑分层）

// 类型化：直接编解码结构化对象
var root = new ZdValue.Map(new Dictionary<string, ZdValue>
{
    ["width"] = new ZdValue.Integer(1920),
    ["name"]  = new ZdValue.String("照片"),
    ["list"]  = new ZdValue.Array(new ZdValue[]
    {
        new ZdValue.Integer(1),
        new ZdValue.Float(2.5),
    }),
});
byte[] bytes = ZdCodec.Encode(root);   // 写成 zd 字节
ZdValue back = ZdCodec.Decode(bytes);  // 读回

// 或直接映射 CLR 对象
byte[] enc = ZdCodec.Encode(ZdValue.FromObject(new Dictionary<string, object?>
{
    ["id"]    = 123,
    ["ratio"] = 0.5,
    ["flags"] = new object?[] { true, false, 7 },
}));

// 文件（带 TIEDBZD 魔数头）
Zd.Save("out.zd", bytes);
byte[] loaded = Zd.Load("out.zd");     // 校验魔数、去头返回正文
```

## 格式互转（zd ↔ 常见格式）

所有互转都以 `ZdValue` 为统一中转模型，经 `ZdConvert` 门面一键调用，
已支持 **JSON / tie:data / 字节 / base64 / CSV / INI / XML / YAML / TOML**。

| 格式 | 进（→ ZdValue） | 出（ZdValue →） | 支持内容 | 边界 / 说明 |
|---|---|---|---|---|
| JSON | `JsonToValue` | `ValueToJson` | object / array / 字符串 / 数字 / 布尔 / null | 完整解析器 + 紧凑/可读输出；null→`Null` 哨兵 |
| tie:data | `TieDataToValue` | `ValueToTieData` | 同 JSON（JSON 子集） | 等价 JSON 解析；输出为 pretty JSON |
| 字节 | `BytesToValue` | `ValueToBytes` | byte[] ⇄ zd 数组(0..255) | 每字节一个 Integer |
| base64 | `Base64ToValue` | `ValueToBase64` | base64 ⇄ zd 数组 | 经字节中转 |
| CSV | `CsvToValue` | `ValueToCsv` | 二维表（行/列，格为 String） | RFC 4180：引号/逗号/换行转义；表头算一行 |
| INI | `IniToValue` | `ValueToIni` | 段 → 键值 Map | 全局段 key=`""`；注释 `;` `#` |
| XML | `XmlToValue` | `ValueToXml` | 属性/子元素/文本/重复元素→数组 | 根需指定名；`@属性`、`#text` 约定 |
| YAML | `YamlToValue` | `ValueToYaml` | 块映射/序列、流式 `[]{}`、块标量 | 缩进子集；不解析锚点/别名/标签 |
| TOML | `TomlToValue` | `ValueToToml` | 表/数组表/点分键/数组/内联表 | 日期/时间存为 String；根为 Map |

```csharp
// JSON ↔ zd 值
ZdValue v = ZdConvert.JsonToValue("""{"name":"照片","w":1920}""");
string json = ZdConvert.ValueToJson(v);                 // 紧凑 JSON

// tie:data（JSON 子集，可读输出）
string data = ZdConvert.ValueToTieData(v);              // pretty JSON

// 字节 / base64
byte[] raw  = [0x01, 0x02, 0xFF];
ZdValue arr = ZdConvert.BytesToValue(raw);              // zd 数组
string b64  = ZdConvert.ValueToBase64(arr);

// CSV / INI / XML / YAML / TOML / JSON→zd 字节
ZdValue tbl = ZdConvert.CsvToValue("a,b\n1,2\n");
ZdValue ini = ZdConvert.IniToValue("[sec]\nk=v\n");
string xml  = ZdConvert.ValueToXml(v, "root");
ZdValue yml = ZdConvert.YamlToValue("name: 张三\nage: 30\n");
ZdValue tml = ZdConvert.TomlToValue("[server]\nhost = \"h\"\n");
byte[] zd   = ZdConvert.JsonToBytes("""{"id":7}""");
```

> JSON 的 `null` 在 zd 中无对应标签：解析会得到 `ZdValue.Null` 哨兵（可输出回
> `null`），但编码成 zd 字节会抛异常提示。

## 进阶能力

除原语 / 类型化 / 格式互转之外，DoNetZD 还内置一组实用能力（零依赖，全部 `netstandard2.0` 可用）。

### 流式编解码 + 异步 IO

`ZdStream.Encode(Stream, ZdValue)` / `ZdStream.Decode(Stream)` 直接在流上读写，
不把整个结构物化为单个 `byte[]`，适合大体积嵌套数据流过管线；输出字节与
`ZdCodec.Encode` 逐字节一致。`Zd.SaveAsync` / `Zd.LoadAsync` 提供异步文件 IO。

```csharp
using var ms = new MemoryStream();
ZdStream.Encode(ms, root);          // 直写流
ms.Position = 0;
ZdValue back = ZdStream.Decode(ms); // 直读流

await Zd.SaveAsync("out.zd", body);
byte[] body = await Zd.LoadAsync("out.zd");
```

### CRC32 校验文件

`ZdCrc32.Compute`（IEEE 802.3，自实现）可独立用于任意字节校验和；
`Zd.SaveChecked` / `Zd.LoadChecked` 写入并验证 “魔数 + 4B CRC + body” 文件，
篡改一字节即抛 `InvalidDataException`，用于关键数据落盘完整性。

```csharp
Zd.SaveChecked("cfg.zd", body);     // 带 CRC 写入
byte[] ok = Zd.LoadChecked("cfg.zd"); // 校验通过；篡改则抛异常
```

### POCO 反射绑定

`ZdSerializer.Serialize<T>` / `Deserialize<T>` 把任意 POCO 与 zd 字节互转。
扫描 public 字段 + 可读属性，用 `[ZdName]` 改键名、`[ZdIgnore]` 跳过成员；
枚举↔Integer，`List<T>` / 数组 / `Dictionary<string,V>` / 嵌套 POCO 递归。
也可经 `ZdValue.FromObject` / `value.ToObject<T>()` 直接走绑定。

```csharp
public class Config
{
    public int Id { get; set; }
    [ZdName("display_name")] public string Name { get; set; } = "";
    [ZdIgnore] public string Secret { get; set; } = "";
    public List<int> Tags { get; set; } = new();
}

byte[] bytes = ZdSerializer.Serialize(cfg);
Config back = ZdSerializer.Deserialize<Config>(bytes);

// 或就地转
ZdValue v = ZdValue.FromObject(cfg);   // POCO → Map（尊重特性）
Config c2 = v.ToObject<Config>()!;      // Map → POCO
```

### 路径访问与写回

`ZdPath.Get(root, "users[0].name")` 按点分键 + `[索引]` 取嵌套值；
支持可选 `$` 前缀与 `['带引号键']`。找不到返回 `null` / `TryGet` 返回 `false`。

`ZdPath.Set(root, path, value)` 按路径写回，返回新的根（不可变重建，原根不变，
用 `root = ZdPath.Set(root, path, v)` 写回）。规则：
Map 键叶子可新增/替换、中间键缺失自动建链；Array 叶子处 `i==Count` 追加、
越界失败；段类型不匹配失败。纯 Key 链也支持从 `null` 根建树。
`TrySet` 返回 bool，避免抛异常。

可选开关 `fillGaps: true` 启用宽松模式：数组段越界自动扩容，空洞填
`ZdValue.Null` 哨兵，中间越界位置预建下一段容器（Key→Map / Index→Array）。
注意 Null 哨兵不能编码为 zd 字节（仅限模型内操作与 JSON 等含 null 格式输出）。

```csharp
ZdValue? host = ZdPath.Get(root, "server.host");
ZdValue? name = ZdPath.Get(root, "$.users[1].name");
ZdPath.TryGet(root, "a.b[2].c", out var hit);

root = ZdPath.Set(root, "server.port", new ZdValue.Integer(9090));  // 替换
root = ZdPath.Set(root, "cache.ttl", new ZdValue.Integer(600));     // 中间 cache 自动创建
root = ZdPath.Set(root, "tags[1]", new ZdValue.String("X"));        // 数组元素替换
ZdPath.TrySet(root, "users[9]", new ZdValue.Integer(1), out _);     // 越界 → false
root = ZdPath.Set(root, "tags[4]", new ZdValue.String("Gap"), fillGaps: true);
// tags 扩容到 5，[2][3] 为 Null 哨兵
```

### 深度比较 / 合并 / 遍历

`DeepEquals` 递归比较类型与值；`GetHashCode` 深度哈希（可入哈希容器）；
`Merge(patch)` 实现 RFC 7396 合并补丁（Null 删键、同为 Map 递归、否则替换，返回新值）；
`Visit(action)` 先序遍历整棵值树。

```csharp
bool same = a.DeepEquals(b);
int hash = a.GetHashCode();                 // 容器深度哈希

ZdValue merged = base_.Merge(patch);        // 增量更新，原值不变
int n = 0; root.Visit(_ => n++);            // 数节点
```

### 字节可视化转储

`ZdDump.Dump(bytes)` 把 zd 字节（可带魔数头）渲染为 “偏移 [十六进制] 类型 注解”
的缩进文本树，用于调试与跨语言字节布局对账；经 `ZdConvert.Dump` 也可调用。

```
@0  [TIEDBZD v1] magic ok (8 bytes)
@8  map(2)
  @9  key "port":
    @15 [0xCD] u16 8080
  @18  key "host":
    @24 [str 7B] "0.0.0.0"
```

## 目录结构

```
DoNetZD/
├── docs/2026-08-24-donetzd-design.md   # 设计文档（格式规格 + API 取舍）
├── src/DoNetZD/                        # 库（netstandard2.0）
│   ├── Zd.cs                           # 原语层：编码 / 字节工具 / varint / 文件魔数 / 异步 IO / CRC 文件
│   ├── ZdValue.cs                      # 类型化值模型 + CLR 对象映射 + DeepEquals/Merge/Visit/ToObject
│   ├── ZdCodec.cs                      # 类型化层：递归 Encode / Decode
│   ├── ZdStream.cs                     # 流式编解码（直读写 Stream）+ 异步文件 IO
│   ├── ZdCrc32.cs                      # CRC32（IEEE 802.3）自实现
│   ├── ZdDump.cs                       # zd 字节可视化转储（偏移 + 类型注解）
│   ├── ZdPath.cs                      # 路径访问（点分键 + [索引]）
│   ├── ZdSerializer.cs                 # POCO 反射绑定 + ZdName/ZdIgnore 特性
│   ├── ZdConvert.cs                    # 互转门面（JSON/字节/base64/CSV/INI/XML/YAML/TOML/Dump）
│   ├── JsonCodec.cs                    # JSON ↔ ZdValue
│   ├── BytesCodec.cs                   # 字节 / base64 ↔ ZdValue
│   ├── CsvCodec.cs                     # CSV ↔ ZdValue
│   ├── IniCodec.cs                     # INI ↔ ZdValue
│   ├── XmlCodec.cs                     # XML ↔ ZdValue
│   ├── YamlCodec.cs                    # YAML ↔ ZdValue（块/流式/缩进子集）
│   └── TomlCodec.cs                    # TOML ↔ ZdValue（表/数组表/点分键）
└── tests/DoNetZD.Tests/                # 测试：golden 字节向量 + 回环 + 格式互转 + 流式/POCO/查询/Dump/CRC
```

## 构建与测试

```bash
dotnet build DoNetZD.slnx   # 库 0 错误
dotnet test  DoNetZD.slnx   # 全部通过
```

## 参考

- tie:zd 官方实现：`F:\Projects\tie-repo\tie-main\tieDB\persist\zd.tie`
- 格式规范：`tieDB` 设计文档 §2（MessagePack + Protobuf 混合思路）

## 许可证

DoNetZD 采用 **TIE-LANG Open Source License v1.1** 发布，归 **TIE-LANG organization**
所有。本项目的源码（含本仓库所有 `.cs` / 文档 / 构建文件）即许可证定义的
"The Software" 的组成部分，使用、修改、分发须遵守其第 3 节规定的归属义务（保留
版权声明、随附或链接到许可证全文）。

> 依据该许可证，你**为本软件制作的程序**不受本许可证约束，也无需附带版权声明或
> 许可证副本；但一旦复制或并入本软件的源码，即适用上述条款。

完整条款见 `LICENSE`。