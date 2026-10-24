using System.Diagnostics;
using System.Security.Cryptography;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.Localization;
using NPKStudio.Desktop.ViewModels;
using NPKStudio.Desktop.Views;

using Xunit;
using Xunit.Abstractions;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     帧级变更标记的验收：替换 / 插入 / 移动 / 改尺寸各置对应标记，
///     <b>撤销后必须消失</b>（这正是不在编辑时置位、而按基线重算的原因），保存后全部归零。
///     <para>
///         另外两条硬要求也在这里锁住：标记<b>不改变写出的字节</b>、500 帧规模下重算<b>足够快</b>。
///     </para>
///     <para>
///         <b>换图为什么走 Core 而不是文件</b>：无头宿主用 <c>UseHeadlessDrawing</c>（没有 Skia 编解码器），
///         <c>ImageIo.Load</c> 解不了 PNG，所以"替换素材"这一项在模型层验证（标记规则完全一致）；
///         真实的"选 PNG → 替换 → 角标出现"由截图那次真机运行验证。
///     </para>
/// </summary>
public sealed class FrameChangeMarkerTests
{
    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    private readonly ITestOutputHelper _output;

    public FrameChangeMarkerTests(ITestOutputHelper output) => _output = output;

    /// <summary>替换素材 → 该帧含 <c>Pixels</c>；撤销后标记消失（基线重算的核心价值）。</summary>
    [Fact]
    public void ReplacingFrameImage_MarksPixels_AndUndoClearsIt()
    {
        var image = CreateImage("replace.img", frameCount: 4, width: 8, height: 8);
        var cards = image.Frames.Select(frame => new FrameViewModel(frame)).ToList();
        var history = new ImgEditHistory(image);
        var tracker = new FrameChangeTracker();

        tracker.Capture(image);
        tracker.Refresh(image, cards);
        Assert.Equal(0, tracker.ChangedCount);

        image.ReplaceFrameImage(image.Frames[1], Solid(8, 8), ColorBits.UNKNOWN);
        history.Record();
        tracker.Refresh(image, cards);

        Assert.True(cards[1].ChangeKind.HasFlag(FrameChangeKind.Pixels));
        Assert.True(cards[1].HasChanges);
        Assert.True(cards[1].ShowPixelsBadge);
        Assert.Equal(1, tracker.ChangedCount);

        Assert.True(history.Undo());
        tracker.Refresh(image, cards);

        Assert.Equal(FrameChangeKind.None, cards[1].ChangeKind);
        Assert.Equal(0, tracker.ChangedCount);
    }

    /// <summary>
    ///     插入帧 → 新帧含 <c>Inserted</c>；<b>被整体平移的后续帧不能跟着被标成 <c>Moved</c></b>。
    ///     <para>这一条是"插入=全员移动"这个常见坑的回归测试：漏了补偿的话，在列表头插一帧就等于给整个文件打满标记。</para>
    /// </summary>
    [AvaloniaFact]
    public async Task InsertingFrame_MarksOnlyTheNewFrame_AndUndoClearsIt()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.SelectedFrame = vm.Frames[1];
        vm.InsertFrameAfterSelected();

        Assert.Equal(5, vm.Frames.Count);
        var inserted = vm.Frames[2];
        Assert.True(inserted.ChangeKind.HasFlag(FrameChangeKind.Inserted));
        Assert.Equal(1, vm.ChangedFrameCount);
        Assert.Contains(Loc.F("panel.changedSuffix", 1), vm.FrameCounter);

        for (var i = 0; i < vm.Frames.Count; i++) {
            if (i == 2) continue;
            Assert.Equal(FrameChangeKind.None, vm.Frames[i].ChangeKind);
        }

        vm.UndoEdit();

        Assert.Equal(4, vm.Frames.Count);
        Assert.Equal(0, vm.ChangedFrameCount);
        Assert.All(vm.Frames, frame => Assert.False(frame.HasChanges));
    }

    /// <summary>上移一帧 → 交换的两帧都含 <c>Moved</c>；撤销后消失。</summary>
    [AvaloniaFact]
    public async Task MovingFrame_MarksMoved_AndUndoClearsIt()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var moved = vm.Frames[2];
        vm.SelectedFrame = moved;
        vm.MoveSelectedFrame(-1);

        // 卡片按 ImgFrame 引用复用：移动后对象还是同一个，只是位置变了
        Assert.Same(moved, vm.Frames[1]);
        Assert.True(moved.ChangeKind.HasFlag(FrameChangeKind.Moved));
        Assert.True(vm.ChangedFrameCount > 0);

        vm.UndoEdit();

        Assert.Same(moved, vm.Frames[2]);
        Assert.Equal(FrameChangeKind.None, moved.ChangeKind);
        Assert.Equal(0, vm.ChangedFrameCount);
    }

    /// <summary>改宽高（走检查器，与用户输入数字是同一条路径）→ 含 <c>Geometry</c>。</summary>
    [AvaloniaFact]
    public async Task ResizingFrame_MarksGeometry()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.SelectedFrame = vm.Frames[0];
        vm.Inspector.Width = 12;

        Assert.Equal(12, vm.Frames[0].Frame.Width);
        Assert.True(vm.Frames[0].ChangeKind.HasFlag(FrameChangeKind.Geometry));
        Assert.Equal(1, vm.ChangedFrameCount);

        vm.UndoEdit();

        Assert.Equal(8, vm.Frames[0].Frame.Width);
        Assert.Equal(FrameChangeKind.None, vm.Frames[0].ChangeKind);
    }

    /// <summary>插入后又替换素材 → 同一帧同时带 <c>Inserted</c> 与 <c>Pixels</c>（Flags 的意义所在）。</summary>
    [AvaloniaFact]
    public async Task InsertedFrameThatIsThenReplaced_CarriesBothFlags()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.SelectedFrame = vm.Frames[0];
        vm.InsertFrameAfterSelected();

        var inserted = vm.Frames[1];
        vm.CurrentImgFile!.ReplaceFrameImage(inserted.Frame, Solid(8, 8), ColorBits.UNKNOWN);
        vm.RefreshChangeMarkers();

        Assert.True(inserted.ChangeKind.HasFlag(FrameChangeKind.Inserted));
        Assert.True(inserted.ChangeKind.HasFlag(FrameChangeKind.Pixels));

        // 角标只显示"新增"，其余在 tooltip 里列全
        Assert.True(inserted.ShowInsertedBadge);
        Assert.False(inserted.ShowPixelsBadge);
        Assert.Contains(Loc["frame.change.pixels"], inserted.ChangeTooltip!);
        Assert.Contains(Loc["frame.change.inserted"], inserted.ChangeTooltip!);
        Assert.Equal(1, vm.ChangedFrameCount);
    }

    /// <summary>保存成功后重新拍基线：所有标记归零，计数与「改 N」一起消失。</summary>
    [AvaloniaFact]
    public async Task Saving_ClearsEveryMarker()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.SelectedFrame = vm.Frames[2];
        vm.MoveSelectedFrame(-1);
        vm.SelectedFrame = vm.Frames[0];
        vm.InsertFrameAfterSelected();
        vm.Inspector.Width = 12;

        Assert.True(vm.ChangedFrameCount > 0);

        await vm.SaveAsync();

        Assert.Equal(0, vm.ChangedFrameCount);
        Assert.All(vm.Frames, frame => Assert.Equal(FrameChangeKind.None, frame.ChangeKind));
        Assert.DoesNotContain(Loc.F("panel.changedSuffix", 1), vm.FrameCounter);
        Assert.False(vm.IsDirty);
    }

    /// <summary>
    ///     标记不影响输出：<b>标记还在的时候</b>保存出来的字节，与直接让 Core 编码同一份模型逐字节一致。
    ///     <para>标记只存在于 <see cref="FrameViewModel" />（UI 包装）上，编码链路看不到它——这条断言把它锁死。</para>
    /// </summary>
    [AvaloniaFact]
    public async Task ChangeMarkers_DoNotAffectSavedBytes()
    {
        using var folder = TestFolder.Create();
        var path = folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.SelectedFrame = vm.Frames[1];
        vm.InsertFrameAfterSelected();
        vm.SelectedFrame = vm.Frames[3];
        vm.MoveSelectedFrame(-1);

        Assert.True(vm.ChangedFrameCount > 0);

        // 此刻标记仍然挂着：先记下"纯 Core 编码"的结果，再走 UI 保存，两边必须一致
        var coreBytes = Sha256(vm.CurrentImgFile!.ToArray());
        await vm.SaveAsync();

        Assert.Equal(coreBytes, Sha256(File.ReadAllBytes(path)));
    }

    /// <summary>隐藏是纯视图状态：与变更标记互不影响（截图里那两张卡片的逻辑前提）。</summary>
    [AvaloniaFact]
    public async Task HiddenFrame_KeepsItsChangeMarker()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.Frames[3].IsVisible = false;
        vm.RefreshFrameVisibility();

        vm.SelectedFrame = vm.Frames[3];
        vm.MoveSelectedFrame(-1);

        Assert.False(vm.Frames[2].IsVisible);
        Assert.True(vm.Frames[2].ChangeKind.HasFlag(FrameChangeKind.Moved));

        // 上移一位是和相邻帧"交换位置"：两帧的相对顺序都变了，所以两帧都带 Moved
        Assert.Equal(2, vm.ChangedFrameCount);
    }

    /// <summary>
    ///     界面层：卡片真的挂上了 <c>changed</c> 样式类（编译绑定 + 样式选择器的回归）。
    ///     <para>纯 VM 测试看不出 <c>x:DataType</c> 写错或样式选择器不匹配，所以这一层必须单独验。</para>
    /// </summary>
    [AvaloniaFact]
    public async Task FrameCard_ShowsChangedClass()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);

        vm.SelectedFrame = vm.Frames[0];
        vm.InsertFrameAfterSelected();
        window.UpdateLayout();

        var list = window.FindControl<ListBox>("FrameList")!;
        var container = list.ContainerFromIndex(1);
        Assert.NotNull(container);

        var changedCards = container!.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("changed"))
            .ToList();
        Assert.NotEmpty(changedCards);

        window.Close();
    }

    /// <summary>
    ///     性能：500 帧、96×96（每帧 18 KB 原始字节）下重算一次 &lt; 10 ms。
    ///     <para>
    ///         常态刷新（移动 / 插入 / 调坐标）走"数组引用相同"的极速路径，零哈希；
    ///         这里额外记录"全部 500 帧都被换过素材"的病态耗时，只做 &lt; 50 ms 的兜底断言。
    ///     </para>
    /// </summary>
    [Fact]
    public void RefreshFiveHundredFrames_StaysUnderThreshold()
    {
        const int count = 500;
        const int width = 96;
        const int height = 96;

        var image = CreateImage("bench.img", count, width, height);
        var cards = image.Frames.Select(frame => new FrameViewModel(frame)).ToList();
        var tracker = new FrameChangeTracker();
        tracker.Capture(image);
        tracker.Refresh(image, cards);      // 预热：JIT + 首次字典扩容

        // ① 稳态：模型没变，标记应当全部是 None
        var steady = TimeBest(() => tracker.Refresh(image, cards));
        Assert.Equal(0, tracker.ChangedCount);

        // ② 常态编辑：只换了一帧的素材
        image.Frames[250].RawData = new byte[width * height * 2];
        var single = TimeBest(() => tracker.Refresh(image, cards));
        Assert.Equal(1, tracker.ChangedCount);

        // ③ 病态：500 帧全部换过素材（第一次要真算 500 次 SHA-256，共 ~9 MB）
        for (var i = 0; i < count; i++) {
            image.Frames[i].RawData = new byte[width * height * 2];
            image.Frames[i].RawData[0] = (byte)(i + 1);
        }

        var firstAll = Stopwatch.StartNew();
        tracker.Refresh(image, cards);
        firstAll.Stop();
        Assert.Equal(count, tracker.ChangedCount);

        // 之后同样的状态再刷（引用没变，走哈希缓存）
        var all = TimeBest(() => tracker.Refresh(image, cards));
        Assert.Equal(count, tracker.ChangedCount);

        _output.WriteLine($"500 帧（96×96）重算耗时：稳态 {steady.TotalMilliseconds:F3}ms / " +
                          $"单帧换素材 {single.TotalMilliseconds:F3}ms / " +
                          $"全量换素材首次 {firstAll.Elapsed.TotalMilliseconds:F3}ms / " +
                          $"全量换素材再刷 {all.TotalMilliseconds:F3}ms");

        Assert.True(steady.TotalMilliseconds < 10,
            $"500 帧稳态重算应当 < 10ms，实测 {steady.TotalMilliseconds:F2}ms");
        Assert.True(single.TotalMilliseconds < 10,
            $"500 帧单帧变更后重算应当 < 10ms，实测 {single.TotalMilliseconds:F2}ms");
        Assert.True(all.TotalMilliseconds < 50,
            $"500 帧全量换素材的病态重算应当 < 50ms，实测 {all.TotalMilliseconds:F2}ms");
        Assert.True(firstAll.Elapsed.TotalMilliseconds < 50,
            $"500 帧全量换素材（含 9MB 哈希）首次重算应当 < 50ms，实测 {firstAll.Elapsed.TotalMilliseconds:F2}ms");
    }

    /// <summary>建一个与 <see cref="TestFolder" /> 同构的内存 IMG（不落盘，避免 500 帧用例写文件）。</summary>
    private static ImgFile CreateImage(string name, int frameCount, int width, int height) {
        var image = ImgFile.Create(ImgVersion.Ver2, name);
        for (var i = 0; i < frameCount; i++) {
            var frame = image.AddFrame();
            frame.Type = ColorBits.ARGB_1555;
            frame.Compress = CompressMode.ZLIB;
            frame.Width = width;
            frame.Height = height;
            frame.FrameWidth = width;
            frame.FrameHeight = height;
            frame.X = i;
            frame.RawData = new byte[width * height * 2];
            frame.RawData[0] = (byte)i;
            frame.RawData[1] = (byte)(i >> 8);
        }

        return image;
    }

    /// <summary>一张纯色图（Core 侧换图素材，不需要任何图片编解码器）。</summary>
    private static RgbaImage Solid(int width, int height) {
        var image = new RgbaImage(width, height);
        for (var i = 0; i < image.Pixels.Length; i += 4) {
            image.Pixels[i] = 0x20;
            image.Pixels[i + 1] = 0x60;
            image.Pixels[i + 2] = 0xC0;
            image.Pixels[i + 3] = 0xFF;
        }

        return image;
    }

    /// <summary>跑 5 次取最快的一次（避开 JIT / GC 偶发抖动，测的是算法本身）。</summary>
    private static TimeSpan TimeBest(Action action) {
        var best = TimeSpan.MaxValue;
        for (var i = 0; i < 5; i++) {
            var watch = Stopwatch.StartNew();
            action();
            watch.Stop();
            if (watch.Elapsed < best) best = watch.Elapsed;
        }

        return best;
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
