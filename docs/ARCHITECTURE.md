# 架构说明

面向准备改这个仓库的人。读完应该能回答三个问题：代码分成哪几块、状态到底存在哪、哪些写法会破坏性能或绑定。

> 先看 [README](../README.md) 了解产品形态。本文只讲实现，不重复使用说明。

## 0. 一分钟版本

```
┌─────────────────────────────────────────────────────────┐
│ NPKStudio.Desktop  (Avalonia 11 / net10.0 / WinExe)      │
│  Views ── MainWindow.axaml + 两个对话框（编译绑定）       │
│  ViewModels ── MainWindowViewModel（外壳）                │
│              └─ Channels/（每通道一份状态）               │
│  Controls ── ImageCanvas + Rendering/（只画，不管数据）   │
│  Services ── 设置 / 清单 / 打包 / 导出 / 版本策略         │
└──────────────────────────┬──────────────────────────────┘
                           │ 单向依赖（Desktop → Core）
┌──────────────────────────▼──────────────────────────────┐
│ NPKStudio.Core  (类库 / net10.0 / 零 UI / 零第三方包)     │
│  Formats ── ImgFile + 版本 Handler、NpkArchive / Writer  │
│  Imaging ── RgbaImage、颜色编解码、ZLib、DXT、GIF         │
│  Diagnostics / Exceptions ── 日志与领域异常               │
└─────────────────────────────────────────────────────────┘
```

依赖方向**只有一条**：`Desktop → Core`。Core 不引用 Avalonia，也不知道"窗口""选中帧""主题"这些概念。

## 1. 格式层（`src/NPKStudio.Core`）

Core 的职责是"字节 ↔ 内存模型"，不碰 UI 状态，也不决定"要不要警告用户"。

### 1.1 IMG

| 类型 | 职责 | 边界 |
| --- | --- | --- |
| `Formats/ImgFile` | 一个 `.img` 的内存模型：版本、`IndexLength`、帧列表、调色板/纹理表 | 纯 POCO，不实现 `INotifyPropertyChanged`，可跨线程传递；只做"魔数探测 + 公共头"，版本细节交给 Handler |
| `Formats/IImgHandler` | 单个版本的读写策略：`ReadIndex` / `Decode` / `Encode` / `WriteIndex` / `WritePayload` | 版本特有的头、索引区、调色板表、纹理表都在这里；无状态，每次取新实例 |
| `Formats/Handlers/Ver1/Ver2/Ver4/Ver5/Ver6Handler` | v1 / v2 / v4 / v5 / v6 的具体实现 | v1 的索引与数据内联；v4/v5/v6 的调色板与纹理表在版本头里 |
| `Formats/Handlers/OtherHandler` | 未实现版本的兜底：整段取样，不解析帧 | v7/v8/v9 走这里，帧编辑一律不可用 |
| `Formats/ImgHandlerRegistry` | 版本 → Handler 工厂；`Get` 走无锁的只读快照，重复注册幂等（D-08） | 未注册版本回落到 `OtherHandler`，同版本只告警一次 |
| `Formats/ImgFrame` + `FrameTable` / `FrameGeometry` / `FrameLinks` | 帧元数据、帧域、LINK 关系 | 帧的像素原始字节保存在帧上，写回时原样使用 |
| `Formats/FramePixelEncoder` / `FrameTrimmer` / `PaletteIndexCodec` | 像素格式编码、透明边裁剪、调色板索引 | 编解码的幂等性有单测；LINK 帧不重复编码（D-04） |

### 1.2 NPK

| 类型 | 职责 | 边界 |
| --- | --- | --- |
| `Formats/Npk/NpkArchive` | 只读打开 `.NPK`：条目表解析、按偏移随机读、已解析 IMG 的 LRU(8) 缓存 | 全部读取走 `RandomAccess.Read`，不共享 `Stream` 游标，可并发；不负责写 |
| `Formats/Npk/NpkWriter` | 从条目列表**流式**构建 NPK | 偏移只依赖长度，因此先写完整条目表再逐条拷数据，峰值内存与包大小无关 |
| `Formats/Npk/NpkEntrySource` | "长度先给出、内容后写"的条目抽象类 | 用抽象类而非 `Action<Stream>`，避免每条目一个闭包；写入字节数必须恰好等于 `Length` |
| `Formats/Npk/NpkCipher` | 256 字节路径的加解密 | 与 `NpkArchive` / `NpkWriter` 共用同一组常量 |
| `Formats/Npk/NpkEntryTable` / `NpkRawReader` / `NpkImageCache` | 布局常量、随机读取器、缓存 | 数据区起点 `52 + 264 × count`，不是 53 |

### 1.3 像素与编码

`Imaging/RgbaImage` 是唯一的内存位图：**BGRA8888、直通 alpha（非预乘）、stride 固定 `Width * 4`**。
`Pixels` 暴露底层数组以避免拷贝，但从不改变数组长度。

- `ColorCodec` / `ColorBits`：ARGB1555 / ARGB4444 / BGRA8888 等格式互转
- `ZlibCodec`：基于 `System.IO.Compression.ZLibStream`，与原 zlib 的 `78 9C` 流兼容
- `DxtDecoder` / `DdsFile`：DXT1/3/5 解码（v5 纹理页）
- `Gif/GifEncoder`：GIF89a 编码，自带 LZW
- 全部零第三方包：压缩用 BCL，JSON 用 `System.Text.Json`

## 2. UI 层（`src/NPKStudio.Desktop`）

### 2.1 画布：`Controls/ImageCanvas`（699 行）

控件本身只做三件事：

1. 声明 Avalonia `StyledProperty`（图像、背景模式、标尺开关、主题画刷、`CanvasSize` / `FrameOffset` / `CanvasOrigin` 等）
2. 持有并组装渲染协作者（`_backgroundRenderer` / `_frameRenderer` / `_rulerRenderer` / `_guideRenderer` / `_crosshairRenderer` + `_transform` / `_pixelZoom` / `_guides` / `_guideInteraction`）
3. 分发输入事件（缩放、平移、标尺拖拽、参考线拖拽、右键菜单），并调用 `InvalidateVisual`

它**不**知道 NPK、IMG、帧列表、撤销栈；给它一张 `RgbaImage` 和一个包围盒，它就画。

`Controls/Rendering/` 共 8 个文件：

| 文件 | 行数 | 职责 |
| --- | --- | --- |
| `CanvasBackgroundRenderer` | 40 | 帧外底色 + 帧内棋盘格/纯色 |
| `FrameRenderer` | 88 | 当前帧位图与 1px 边框 |
| `RulerRenderer` | 253 | 顶部/左侧标尺、刻度与读数（含步长选择） |
| `GuideRenderer` | 402 | 参考线 + 坐标标签（含标签防重叠与文本缓存） |
| `CrosshairRenderer` | 55 | 鼠标十字虚线 |
| `CanvasRenderContext` | 108 | 一次绘制所需的全部只读参数（`readonly struct`，`in` 传递） |
| `GuideStore` | 58 | 参考线的存储与增删 |
| `GuideInteraction` | 165 | 参考线拖拽状态机 |

关键点：`CanvasRenderContext` 是值类型并按 `in` 传递，渲染路径上不产生额外堆分配；画刷/画笔由 XAML 以属性注入，renderer 不自己 new。

### 2.2 视图模型：外壳 + 通道

**`ViewModels/MainWindowViewModel`（609 行）是外壳，不是"主业务类"。**
它只持有窗口级状态：缩放读数、鼠标坐标、标尺/参考线开关、坐标基准、左右栏折叠。

所有"与某个打开的 IMG 绑定"的状态都在通道里；外壳把约 60 个同名成员转发到 `ActiveChannel`，
并在切换通道时按 `ForwardedProperties` 整批重发 `PropertyChanged`。

**`ViewModels/Channels/` 是真正的业务状态：**

| 类型 | 行数 | 职责 |
| --- | --- | --- |
| `FrameChannel` | 645 | **聚合根**：通道身份（会话/条目表/文件名）、帧集合、选中、状态栏、五个协作者的组装 |
| `ChannelDocument` | 332 | 打开 / 加载 / 保存 / 另存 / 打包 / 资源清单 |
| `ChannelViewState` | 353 | 可见性、搜索过滤、当前图像、包围盒、帧检查器 |
| `ChannelPlayback` | 301 | 本通道的播放机：倍率(9 档)、循环、`DispatcherTimer`、推进 |
| `ChannelFrameEditor` | 245 | 帧结构编辑 + 撤销栈 + 变更标记 |
| `ChannelStatusText` | 80 | 状态栏纯文案 |
| `FrameImageCache` | 244 | 帧位图缓存：按帧引用 + 内容指纹自动失效，预算 64MB |
| `ChannelCollection` | 83 | 通道顺序表 + `ActiveChannel`（永不为 null） |

规则：协作者**不互相持有**。需要读/写对方状态时，由聚合根注入裸委托（例如
`ChannelPlayback.CurrentFrame` / `ShowFrame`），或由聚合根统一转发属性通知。
`ChannelPlayback` 因此不知道 `FrameChannel` 的存在，也没有任何静态播放状态。

`ChannelCollection` 构造时就建好第一个通道，所以界面永远有可绑定的活动通道；
`Add()` 可以继续追加，`SetActive()` 只切换"看哪个"，不改通道内部的选中/播放/过滤/变更基线。

### 2.3 服务层（`Services/`）

与 UI 无关但需要文件系统的逻辑：`AppSettingsStore`（UTF-8 no BOM 的设置落盘）、
`ResourceManifest` / `ResourceManifestWriter`（资源清单读写）、`NpkPackService`（批量打包）、
`FrameExportService` / `ImageIo`（PNG/GIF 导出）、`FolderEntrySource`（文件夹模式）、
`VersionSupport`（版本支持等级，见 §4）、`Services/TimerResolution`、`CrashReporter`（在项目根目录，不在 `Services/`）。

## 3. 数据流

### 打开

```
拖入文件 / 菜单打开
  → ChannelDocument 判定模式（NPK 包 或 文件夹）
  → NpkArchive.Open（只读映射条目表，不解码）
  → 选中条目：NpkArchive 取该条目字节 → ImgFile.Parse → Handler.ReadIndex
  → 选中帧：Handler.Decode → RgbaImage（后台线程）
  → FrameViewModel.CurrentImage → ImageCanvas.Image
  → InvalidateVisual → Renderer 依次绘制
```

条目列表走 Avalonia 默认虚拟化面板。帧缩略图由后台任务按顺序补齐（最多 80 张）：
帧列表先入列，缩略图逐张回填，加载流程在列表就位之后再等这个任务收尾。

帧像素走另一条链路：`ViewModels/Channels/FrameImageCache` 按帧引用缓存解码结果
（预算 64MB、LRU），并记录取图时的 `RawData` / `Texture` 引用与几何字段作为内容指纹。
命中直接返回，未命中才解码；指纹不匹配（帧被编辑过）时自动失效，因此编辑入口不需要手工清缓存。

### 编辑与写回

```
用户操作（替换/插入/删除/移动/改坐标）
  → ChannelFrameEditor 改 ImgFile / ImgFrame 元数据
  → 被改帧标记 IsModified，并写入 RawData（原始像素字节）
  → 变更标记 + 撤销栈（50 步）
保存
  → Handler.Encode 只重编码被改的帧
  → NpkWriter.WriteStreaming 重建条目表，未改条目按原字节直接拷贝
```

**预览不影响文件**：画布上的背景与"线性减淡"效果只作用于显示副本；
导出 PNG、写回 `.img`、另存 NPK 用的都是原始字节。

## 4. 版本支持策略

`IMG` 版本能不能读是格式事实，**要不要警告用户是产品决定**。
所以判定点 `Services/VersionSupport.cs` 放在 Desktop，不进 Core：

| 等级 | 版本 | 行为 |
| --- | --- | --- |
| `Verified` | v1 / v2 | 有真实样本，读写不提示 |
| `Experimental` | v4 / v5 / v6 | 能读能写，但按旧实现推导、无真实样本；保存前二次确认 |
| `Passthrough` | v7 / v8 / v9 / 非 IMG | 不解析帧，只能整段原样保存 |

拿到真实样本并逐字节验证后，才能把某版本从 `Experimental` 改成 `Verified`。

## 5. 三条设计约束

这三条不是风格偏好，是**性能与可维护性的边界**。改代码前先确认没有越线。

### 5.1 不用接口、不用 DI 容器

- 默认 `internal sealed` + 构造器组合；不引 `Microsoft.Extensions.DependencyInjection`，不做程序集扫描。
- 桌面层没有自定义接口；`Controls/Rendering` 的协作者都是 sealed 类，直接字段持有。
- 两个例外在 Core，且都是"以版本/环境为轴"的稳定策略点：`IImgHandler`（5 个版本处理器 + `OtherHandler` 兜底）
  和 `ILog`（诊断输出）。它们不参与 UI 组装，也不会被换成 mock 以外的实现。
- XAML 与编译绑定要求可见的类型是 `public`（`MainWindow`、`ImageCanvas`、`MainWindowViewModel` 等），
  但它们同样 `sealed`，且内部协作仍走组合。

**为什么**：sealed + 非虚调用能让 JIT 去虚拟化并内联。渲染与播放每秒执行几十次，
接口调用与容器解析在这里是纯开销。

### 5.2 热路径分配纪律

热路径指：`Render`、播放 tick、帧切换、参考线拖拽。

- `Render` 里不 new 画刷/画笔/集合，不写 LINQ，不拼字符串（标签文本走缓存）。
- 绘制参数用 `CanvasRenderContext`（`readonly struct` + `in`）传递。
- 播放 tick 复用现有对象，不每帧创建闭包或 `List`。
- 协作者之间**不发事件**：通知只向上（协作者 → 聚合根/外壳，经 `INotifyPropertyChanged`），
  聚合根向下注入裸委托。事件环会让每次 tick 多一层分配与重入风险。
- 帧像素必须经 `FrameImageCache` 取：一次解码要分配 `W×H×2`（解压缓冲）+ `W×H×4`（BGRA），
  两者都超过 LOH 阈值，逐拍重解会把播放变成 GC 压力。

**为什么**：这些路径的频率由播放倍率决定（4× 时每秒 32 帧）。改动后可量一次
`GC.GetAllocatedBytesForCurrentThread()` 的增量，作为回归依据。

### 5.2.1 缓存的两条不变量

1. **脏条目不淘汰**：`NpkImageCache` 里已置脏（`NpkEntry.IsModified`）的条目永不淘汰，
   MRU 也不淘汰；否则"解析完尚未置脏"的条目会被挤掉，随后的编辑就落在缓存外的对象上。
   容量是软上限：宁可超容量，也不能丢编辑。
2. **脏条目丢失即中止**：`NpkArchive` 发现条目已置脏、内存中的 `ImgFile` 却不存在时抛异常中止保存，
   不退回"按原字节直拷"，避免产出内容缺失但看起来成功的文件。

### 5.3 绑定走编译绑定

- 所有 `.axaml` 使用 `x:DataType`，项目已开启 `AvaloniaUseCompiledBindingsByDefault=true`。
- 不用 `Binding` 反射路径，不在 code-behind 里做运行时反射绑定。
- `JsonSerializerIsReflectionEnabledByDefault=false`：任何仍走反射的 JSON 序列化会在运行时直接抛异常，
  强制使用 source generator 或显式 `JsonTypeInfo`。

**为什么**：编译绑定在构建期校验属性名，重命名会直接编译失败而不是运行时静默失效；
同时避免反射与 trimming 冲突。

## 6. 多通道怎么加

`ChannelCollection` 已经能容纳 N 个通道，`ActiveChannel` 决定当前显示；`ChannelPlayback` 也是每通道一份。
所以新增一个通道只需要：

```csharp
var channel = channels.Add();      // 新通道，独立帧表/选中/播放
channels.SetActive(channel);       // 画布与所有转发属性切到它
```

**现状（诚实说明）**：界面只有一个通道，工具栏没有"新建通道"入口，也没有多通道同屏布局。
"多通道同时播放"是规划中的功能，**尚未实现**；不要把它当成现成能力去写 UI 或文档。

## 7. 构建与验收基线

```bash
dotnet build -m:1      # 必须 0 warning / 0 error
dotnet test            # Core 178 + Desktop 175 = 353，0 失败
                       # 其中 3 个用例依赖不入库的真实客户端样本，见 tests/samples/README.md
```

- `-m:1` 限制并行构建，用于稳定复现基线；CI 也用它。
- Desktop 测试用 `Avalonia.Headless.XUnit`，不弹真实窗口；Windows runner 可直接跑。
- 新增控件/语言/颜色时：文案进 `Localization/*.json`（中英双份），颜色进 `Styles/Tokens.axaml`，
  不要在 XAML 里硬编码色值。

## 8. 明确不要做

- 不要在 Core 引用 Avalonia、`System.Drawing` 或任何 UI 包。
- 不要在 `Render` / tick 里分配对象或写 LINQ。
- 不要为"将来可能换实现"抽接口、工厂、抽象基类（现有 `NpkEntrySource` 是因为有真实分配理由才存在的）。
- 不要引入 DI 容器、MediatR、AutoMapper 一类运行时基础设施。
- 不要用运行时 `Binding`，不要关闭编译绑定。
- 不要在代码里写死版本号：唯一来源是 `src/NPKStudio.Desktop/NPKStudio.Desktop.csproj` 的 `<Version>`。
