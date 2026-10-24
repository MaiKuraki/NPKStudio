# NPK / IMG 字节布局

本文档描述本项目实际读写的字节结构。数值来自 `src/NPKStudio.Core` 的常量与解析代码，
并与仓库自带的合成样本 `tests/samples/synthetic_shapes.NPK` 往返一致。

> **验证范围**：v1 / v2 有真实客户端包的使用记录；v4 / v5 / v6 的结构依据 ExtractorSharp 的实现推导，
> 仓库内没有真机样本可逐字节核对，因此产品上标注为"实验性"，写回前会二次确认。
> 修改任何常量前请先跑 `dotnet test`，并优先补一条往返用例。

---

## 1. NPK 容器

```
偏移            长度              内容
0               16               "NeoplePack_Bill" + 0x00
16              4                int32 count
20              264 × count      条目表，每项：
                                    0   int32 offset     数据区偏移
                                    4   int32 length     数据长度
                                    8   byte[256]        路径（与 256 字节密钥逐字节 XOR，遇 0x00 结束）
20 + 264×n      32               SHA256（对条目表区 len / 17 * 17 字节计算）
52 + 264×n      …                数据区，按条目顺序连续排列
```

- **数据区起点 = 52 + 264 × count**（52 = 16 + 4 + 32），不是 53。
- `offset` 是 int32：整包上限 2 GB；`NpkWriter` 在累加时显式拦截。
- 哈希的 `len / 17 * 17` 整除截断是格式约定，**不是笔误**；"修正"会让既有包全部校验失败。
- 路径密钥：`"puchikon@neople dungeon and fighter "`（37 字节）+ 循环 `"DNF"` 填到第 254 字节 + 末字节 0x00。

## 2. IMG 公共头（v2 系：v2 / v4 / v5 / v6）

```
0    16    "Neople Img File" + 0x00
16   8     int64  IndexLength     索引区字节数（v5 里指"帧索引区"）
24   4     int32  version
28   4     int32  count           帧数
32   …     版本特有头（见 §4）+ 帧索引区 + 数据区
```

## 3. 帧索引项

```
int32 type                        0x0E 1555 / 0x0F 4444 / 0x10 8888 / 0x11 LINK
                                  0x12 DXT1 / 0x13 DXT3 / 0x14 DXT5
若 type == 0x11 (LINK)：           共 8 字节
    int32 targetIndex
否则：                             共 36 字节
    int32 compress                0x05 NONE / 0x06 ZLIB / 0x07 DDS_ZLIB
    int32 width, height           像素尺寸（1..8192）
    int32 length                  数据区字节数（NONE 时按几何推导，忽略索引里的值）
    int32 x, y                    帧在角色坐标系中的左上角（可为负）
    int32 frameWidth, frameHeight 帧域尺寸
```

- `LINK` 帧不占数据区，解码返回 `null`；删除目标帧时必须把引用者改指向存活帧（见 `FrameLinks`）。
- v4 / v6 的像素是 **1 字节色号**（虽然 `type` 字段仍写 0x0E）；宽高与 `length` 的换算必须按 1 字节/像素。

## 4. 版本特有头

### v1（"Neople Image File"，魔数 18 字节）

```
0    18    "Neople Image File" + 0x00
18   4     int32 indexLength      写入恒 0（读侧不依赖）
22   2     int16 保留
24   4     int32 version
28   4     int32 count
32   …     每帧：索引项 + 该帧 length 字节数据（**内联**，无独立数据区）
```

### v5

```
32   4                 int32 textureCount
36   4                 int32 totalLength      语义不明，读侧忽略、写侧原样回放
40   4                 int32 paletteCount
44   4 × paletteCount  色表（R,G,B,A）
     28 × textureCount 纹理表项：version, type, index, length, fullLength, width, height
     IndexLength 字节  帧索引区（纹理帧额外 28 字节 UV：unknown, textureIndex,
                       leftUpX, leftUpY, rightDownX, rightDownY, rotate）
     数据区            先所有纹理页（按表顺序），再所有内联帧（按帧顺序）
```

- v5 的帧像素来自纹理页 + UV 子矩形；写回是 **ARGB1555 重编码**，不是 DXT 保真（当前不实现 DXT 编码）。
- 判别规则：`type < 0x11 && length == 0` 或 `type >= 0x11` → 纹理帧；`type < 0x11 && length != 0` → 内联 v2 式帧。

### v4 / v6

在公共头之后、帧索引之前依次是：`int32 paletteCount` + 色表（v6 可有多次），随后才是帧索引区。
色表字节序为 **R,G,B,A**（依据旧实现，无真机样本核对）。

## 5. 像素格式

| type | 名称 | 每像素 | 位布局（小端 16 位） |
|---|---|---|---|
| 0x0E | ARGB1555 | 2 | `a:1 r:5 g:5 b:5`；5 位扩展 `(v<<3)|(v>>2)` |
| 0x0F | ARGB4444 | 2 | `a:4 r:4 g:4 b:4`；4 位扩展 `v<<4` |
| 0x10 | ARGB8888 | 4 | 直接 B,G,R,A |
| 0x12-0x14 | DXT1/3/5 | — | DDS 头 + 块数据（DXT3/5 先 alpha 块后色彩块） |

编码/解码互逆由 `ColorCodecTests` 锁定：`Encode(Decode(word)) == word` 对全部合法 16 位字成立。

## 6. 防护栏（不是格式限制）

| 项 | 值 | 目的 |
|---|---|---|
| 单帧宽/高上限 | 8192 | 拦截"80 字节文件声明 20000×20000"导致的 763MB 分配 |
| 单帧像素字节上限 | 64 MB | 与 `ZlibCodec.MaxExpectedLength` 对齐 |
| 色表项数上限 | 256（v4/v6）/ 4096（v5，仅展示） | 色号是 1 字节 |
| v5 纹理页数上限 | 4096 | 拦截损坏文件声明巨量纹理表 |

这些上限都有 `HostileInputTests` 覆盖，且断言**在分配之前**抛出 `ImgFormatException`。
