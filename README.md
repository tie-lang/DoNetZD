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

## 目录结构

```
DoNetZD/
├── docs/2026-08-24-donetzd-design.md   # 设计文档（格式规格 + API 取舍）
├── src/DoNetZD/                        # 库（netstandard2.0）
│   ├── Zd.cs                           # 原语层：编码 / 字节工具 / varint / 文件魔数
│   ├── ZdValue.cs                      # 类型化值模型 + CLR 对象映射
│   ├── ZdCodec.cs                      # 类型化层：递归 Encode / Decode
│   ├── ZdConvert.cs                    # 互转门面（JSON/字节/base64/CSV/INI/XML/tie:data）
│   ├── JsonCodec.cs                    # JSON ↔ ZdValue
│   ├── BytesCodec.cs                   # 字节 / base64 ↔ ZdValue
│   ├── CsvCodec.cs                     # CSV ↔ ZdValue
│   ├── IniCodec.cs                     # INI ↔ ZdValue
│   ├── XmlCodec.cs                     # XML ↔ ZdValue
│   ├── YamlCodec.cs                    # YAML ↔ ZdValue（块/流式/缩进子集）
│   └── TomlCodec.cs                    # TOML ↔ ZdValue（表/数组表/点分键）
└── tests/DoNetZD.Tests/                # 测试：golden 字节向量 + 回环 + 格式互转
```

## 构建与测试

```bash
dotnet build DoNetZD.slnx   # 0 错误 0 警告
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