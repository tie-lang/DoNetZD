# DoNetZD（DNZD）设计

> 日期：2026-08-24
> 状态：设计定稿，进入实现
> 一句话：**用 C# 逐字节复刻 tie 的 `tie:zd` 二进制序列化格式**，作为 .NET 宿主与
> tie 插件之间跨语言数据封包的地基。

## 1. 背景与目标

fptp（Osiris）宿主是 .NET/Avalonia。为了今后能用 tie 写插件（编译 `--shared` DLL，
跨边界只允许标量 + string），需要一个宿主侧能与之互通的结构化封包格式。tie 侧已经
用 `tieDB/persist/zd.tie`（`namespace zd`）实现了自有二进制格式 `tie:zd`
（MessagePack 风格：类型标签 + 紧凑编码；Protobuf 风格 varint）。

本项目的目标：用 .NET 忠实实现同一格式，保证编码字节序列与 tie 侧**逐字节一致**，
双向互通，为后续插件桥打底。

官方格式源：`F:\Projects\tie-repo\tie-main\tieDB\persist\zd.tie`；
规范文档：`F:\Projects\tie-repo\tie-main\docs\plans\tiedb.md` §2。

## 2. 交付物

独立解决方案 `DoNetZD`：
- 库项目 `src/DoNetZD`（net10.0，仅依赖 BCL，无第三方包）
- 测试项目 `tests/DoNetZD.Tests`（xunit）
- 命名空间 `DoNetZD`；静态门面 `Zd`（对齐 tie `namespace zd`），类型化模型层 `ZdValue`

## 3. 格式规格（照抄 tie 定稿，保证互通）

每值 = 类型标签 + 值字节。

### 整数
- fixint 正 `0x00-0x7F`（0..127）；fixint 负 `0xE0-0xFF`（-32..-1）
- 定宽标签（大端）：`0xcc`=u8 `0xcd`=u16 `0xce`=u32 `0xcf`=u64 `0xd0`=i8 `0xd1`=i16 `0xd2`=i32 `0xd3`=i64
- `encode_i64` 分层最紧凑优先（对齐 zd.tie：0..127 / -32..-1 / i8 / u8 / i16 / u16 / i32 / u32 / u64(+)/i64(-)）

### 浮点 / 布尔
- `0xca`=f32 `0xcb`=f64（大端 IEEE 754）
- `0xc2`=false `0xc3`=true

### 字符串（长度 = UTF-8 字节数）
- `0xa0-0xbf`=fixstr(≤31) `0xd9`=str8(≤255) `0xda`=str16(≤65535) `0xdb`=str32
- 中文 1 字 3 字节计入长度

### 数组 / map / 元组
- 数组：`0x90-0x9f`=fixarray(≤15) `0xdc`=array16 `0xdd`=array32；map：`0x80-0x8f`=fixmap(≤15) `0xde`=map16 `0xdf`=map32
- 元组 = 数组编码（复用 array 头）

### tie 扩展标签
- `0xc4`=char（i32 码点大端 4 字节；码点<0 按 0）
- `0xc5`=trit（i8 1 字节，取值 -1/0/1）
- `0xc6`=tuple（此处保留扩展，.NET 用数组头承载）

### record / struct 字段（Protobuf 思路）
- 每字段 = `tag(varint: field_number<<3 | wire_type)` + 值
- wire_type：0=varint、1=64bit、2=len-delimited、5=32bit；field_number 1..15 可单字节

### varint（Protobuf 7 位分组）
- `write_varint`：`0x80` 为续位，低位组在前；n<0 返回空（对齐 tie）
- `read_varint`：>10 组（64 位）/ 越界 / 无终结字节 → 哨兵失败

### 文件魔数头
- 8 字节 `[0x54,0x49,0x45,0x44,0x42,0x5A,0x44,0x01]` = `"TIEDBZD"` + v1
- `Save` 写头；`Load` 校验头并去头返回；`IsZd` 校验魔数

## 4. 类型化模型层（API 风格 B）

在原语之上提供递归编解码模型，便于宿主直接组/拆结构化数据：

- 值类型：`ZdInteger`、`ZdFloat`、`ZdBool`、`ZdString`、`ZdTrit`、
  `ZdArray`、`ZdMap`、`ZdChar`（基类 `ZdValue`）
- `ZdCodec.Encode(ZdValue) -> byte[]` / `ZdCodec.Decode(byte[]) -> ZdValue`（按标签递归分发）
- 便捷对象映射：`ZdValue.FromObject(object?)` / `ToObject()`——
  CLR `long/int/short/byte/ulong` → 整数；`double/float` → 浮点；`bool` →
  布尔；`string` → 字符串；`IList` → 数组；`IDictionary<string,?>` → map

## 5. 判定成功 / 错误处理

- 原语解码统一哨兵：失败返回 `false`（out 值无效），对应 tie 的 `新位置=-1`
- 类型化解码：标签不识别 / 字节不足 → 抛 `ZdFormatException`（含位置）

## 6. 验证方式

- 黄金字节向量：手工构造已知 zd 字节，断言编码结果逐字节相等
- 解码回环：Encode→Decode 值不变
- 跨语言互通（后续补）：跑 tie 生成的 zd 字节与 C# 结果比对

## 7. 范围外（v1 不做）

- 与 tie 的跨语言实测比对（先手工向量，实测放插件桥阶段）
- 文件 zstd/brotli 压缩载体（tie 侧另有 compact）

## 8. zd v2 实现（2026-08-31）

在保留 v1 读取兼容的基础上，实现 `zd v2` 规范
（`F:\Projects\tie-repo\tie-main\docs\superpowers\specs\2026-08-31-zd-v2-design.md`）。

### 头部与版本
- v1 头 8 字节 `"TIEDBZD"+0x01`；v2 头 10 字节 `"TIEDBZD" + base48("02") + flags`
  （魔数 7 + 版本 2 + flags 1）。`Zd.DetectVersion` 先判 v1（版本位固定 0x01，最特异），
  再判 v2（两字节 base48 ∈ [0,47]），从而避免把 v1 文件正文当版本位误判。
- flags 位：字典 bit0、列式 bit1、ext bit2、流 bit3、压缩 bit4（规范 §2）+ schema bit5、哈希 bit6（本库扩展）。
- flags 恒存在（缺省 0），与正文无歧义。

### 新增核心类型
- `null` `0xC0`（区分缺失与空值，v1 无此标签）。
- `bytes` `0xC6 + 数组头(长度) + 原始字节`（v1 中 0xc6 旧义 tuple 且从不落盘，无冲突）。
- `ext` `0xD7 + varint(typeTag) + varint(len) + 载荷`（类型标记须非负）。

### 字节构建器与性能
- `ZdBuilder`：单增缓冲 + `Reserve` 预分配；`ZdCodec.Encode` 一次性写入，消除 O(n²)。
- decode_string 一次性 `Encoding.UTF8.GetString` 批量解码（非逐码点拼接）。

### 字符串字典/引用
- `ZdStringPool` 池段（`Dictionary<string,uint>` + 字符串表）；
  新标签 `0xD8 + varint(池内索引)`。`EncodeWithPool/DecodeWithPool` 输出
  `[池段(数组头+唯一字符串)][正文(引用)]`，重复字符串只存一次。

### 列式容器
- `0xD6 + encode_i64(列数) + 每列[列名(str)+类型(str)+encode_i64(列长)] + 各列值`
  （值按列分组、自描述）。`ZdValue.Columnar` + `ZdColumnar.Column`。

### schema 段 + 内容哈希段
- `ZdSchema` `0xC7 + 数组头 + 每字段[字段名(str)+类型(str)]`。
- `ZdContentHash` `0xC1 + algo(0/1) + 0xc6哈希字节`；算法 CRC32（占位）/ SHA256。
- `ZdV2.Encode/Decode` 容器：`[v2头 flags][schema段?][hash段?][正文]`，哈希覆盖正文，读端校验。

### 兼容性
- v2 写入流默认写 v2 头（10 字节）；`Save/Load/SaveChecked/LoadChecked/SaveAsync/LoadAsync`
  统一识别 v1/v2 头并正确去头返回正文。
- v1 文件可读：`DetectVersion` 返回 v1，正文标签（字符串/整数/…）与 v2 兼容（0xc6 仅 v2 视作 bytes）。