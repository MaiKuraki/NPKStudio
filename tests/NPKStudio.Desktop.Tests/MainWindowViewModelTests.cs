using Avalonia.Headless.XUnit;

using NPKStudio.Core.Formats.Npk;
using NPKStudio.Desktop.Controls;
using NPKStudio.Desktop.ViewModels;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     <see cref="MainWindowViewModel" /> 的 5 条关键路径测试（拆分前的行为基线）。
///     <para>
///         这些用例在无头 Avalonia 上跑（<c>[AvaloniaFact]</c>），因此 <c>DispatcherTimer</c>
///         与 <c>Dispatcher.UIThread.Post</c> 都是真实可用的；每条自己建 VM、自己清理临时目录，
///         互不依赖执行顺序。
///     </para>
///     <para>
///         配置目录由 <see cref="TestEnvironment" /> 指到临时目录，不会碰用户真实的
///         <c>%LOCALAPPDATA%\NPKStudio</c>。
///     </para>
/// </summary>
public sealed class MainWindowViewModelTests
{
    /// <summary>
    ///     1) 打开文件夹：条目数、自动选中第一条、帧数正确。
    /// </summary>
    [AvaloniaFact]
    public async Task OpenFolder_SelectsFirstEntry_AndLoadsAllFrames()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 49, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        Assert.Single(vm.Entries);
        Assert.NotNull(vm.SelectedEntry);
        Assert.Same(vm.Entries[0], vm.SelectedEntry);
        Assert.Equal("walk.img", vm.SelectedEntry!.Name);
        Assert.Equal(49, vm.Frames.Count);
        Assert.Equal("1 / 49", vm.FrameCounter);
    }

    /// <summary>
    ///     2) 切换条目：帧列表、画布尺寸、当前图像同步更新。
    /// </summary>
    [AvaloniaFact]
    public async Task SwitchingEntry_UpdatesFramesCanvasSizeAndCurrentImage()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("a_walk.img", frameCount: 49, width: 8, height: 8);
        folder.WriteImg("b_run.img", frameCount: 56, width: 16, height: 16);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);
        Assert.Equal(2, vm.Entries.Count);

        // 第一条：a_walk（49 帧 / 8×8）
        vm.SelectedEntry = vm.Entries[0];
        await vm.PendingFrameLoad!;

        Assert.Equal(49, vm.Frames.Count);
        Assert.Equal(8 + 48, vm.CanvasSize.Width);   // 帧 X 从 0 递增到 48，包围盒 = 宽度 + 最大 X
        Assert.Equal(8, vm.CanvasSize.Height);
        Assert.NotNull(vm.CurrentImage);
        Assert.Equal(8, vm.CurrentImage!.Width);

        // 第二条：b_run（56 帧 / 16×16）
        vm.SelectedEntry = vm.Entries[1];
        await vm.PendingFrameLoad!;

        Assert.Equal(56, vm.Frames.Count);
        Assert.Equal(16 + 55, vm.CanvasSize.Width);
        Assert.Equal(16, vm.CanvasSize.Height);
        Assert.NotNull(vm.CurrentImage);
        Assert.Equal(16, vm.CurrentImage!.Width);
    }

    /// <summary>
    ///     3) 效果切换 → 落盘 → 重新构造 VM 后读回。
    /// </summary>
    [AvaloniaFact]
    public async Task EffectChange_IsPersistedPerImage_AndRestored()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 12, width: 8, height: 8);

        var first = new MainWindowViewModel();
        await first.OpenFolderAsync(folder.Path);
        Assert.Equal(CanvasEffect.None, first.Effect);

        first.Effect = CanvasEffect.LinearDodge;
        first.FlushSettings();   // 显式落盘（生产里由 500ms 防抖触发）

        var json = TestEnvironment.ReadConfig("graphic-effect.json");
        Assert.NotNull(json);
        Assert.Contains("LinearDodge", json!);
        Assert.Contains("walk.img", json!);

        // 重新构造 VM 并打开同一 IMG → 应读回 LinearDodge
        var second = new MainWindowViewModel();
        await second.OpenFolderAsync(folder.Path);
        Assert.Equal(CanvasEffect.LinearDodge, second.Effect);
        Assert.True(second.UseLinearDodge);
    }

    /// <summary>
    ///     4) 倍率切换 → <see cref="MainWindowViewModel.EffectiveFps" /> 正确 + 落盘 + 重开后读回。
    /// </summary>
    [AvaloniaFact]
    public async Task RateChange_UpdatesEffectiveFps_AndPersists()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var first = new MainWindowViewModel();
        await first.OpenFolderAsync(folder.Path);

        Assert.Equal(3, first.RateIndex);          // 默认 1×
        Assert.Equal(8, first.EffectiveFps);

        first.RateIndex = 8;                        // 4×
        Assert.Equal(32, first.EffectiveFps);
        Assert.Equal("4× (32 fps)", first.SelectedRateText);

        var json = TestEnvironment.ReadConfig("settings.json");
        Assert.NotNull(json);
        Assert.Contains("\"playback.rateIndex\": 8", json!);

        // 重新构造 VM（构造函数从设置里恢复倍率）
        var second = new MainWindowViewModel();
        Assert.Equal(8, second.RateIndex);
        Assert.Equal(32, second.EffectiveFps);
    }

    /// <summary>
    ///     5) 搜索过滤：设置后在防抖窗口内<b>不生效</b>，显式触发（<see cref="MainWindowViewModel.ApplyFilter" />）后生效。
    /// </summary>
    [AvaloniaFact]
    public async Task EntryFilter_Debounces_ThenFiltersCorrectly()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("a_walk.img", frameCount: 3, width: 8, height: 8);
        folder.WriteImg("b_run.img", frameCount: 3, width: 8, height: 8);
        folder.WriteImg("c_stand.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        Assert.Equal(3, vm.Entries.Count);
        Assert.Equal(3, vm.FilteredEntries.Count);

        vm.EntryFilter = "walk";

        // 防抖未到期：过滤视图还没变
        Assert.Equal(3, vm.FilteredEntries.Count);

        // 显式触发（等价于 150ms 后的那次 ApplyFilter）
        vm.ApplyFilter();

        Assert.Single(vm.FilteredEntries);
        Assert.Equal("a_walk.img", vm.FilteredEntries[0].Name);
        Assert.Equal("walk", vm.EntryFilter);
    }
}
