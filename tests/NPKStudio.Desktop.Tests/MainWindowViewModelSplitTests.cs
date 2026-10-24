using Avalonia.Headless.XUnit;

using NPKStudio.Desktop.Controls;
using NPKStudio.Desktop.ViewModels;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     针对"VM 拆分"的三条回归：派生属性联动、播放控制、状态栏文案。
///     <para>
///         拆分后这些行为都跨了类（VM 转发 → 子类实现），最容易因漏发通知而静默失效，
///         所以断言落在<b>最终可观察值</b>上，而不是内部字段。
///     </para>
/// </summary>
public sealed class MainWindowViewModelSplitTests
{
    /// <summary>
    ///     6) 派生属性联动：改 <c>Effect</c>/<c>Backdrop</c> 后，文案派生属性要跟着变。
    /// </summary>
    [AvaloniaFact]
    public async Task DerivedTextProperties_FollowEffectAndBackdrop()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        Assert.Equal("原图", vm.SelectedEffectName);

        vm.Effect = CanvasEffect.LinearDodge;
        Assert.Equal("线性减淡", vm.SelectedEffectName);
        Assert.True(vm.UseLinearDodge);

        vm.Effect = CanvasEffect.None;
        Assert.Equal("原图", vm.SelectedEffectName);
        Assert.False(vm.UseLinearDodge);

        // 背景档位的短名（Dark=深灰；CheckerDark=暗格）
        vm.Backdrop = CanvasBackground.Dark;
        Assert.Equal("深灰", vm.BackdropShortName);

        vm.Backdrop = CanvasBackground.CheckerDark;
        Assert.Equal("暗格", vm.BackdropShortName);
    }

    /// <summary>
    ///     7) 播放控制：Toggle 两态切换；播放中切换条目必须自动停止。
    /// </summary>
    [AvaloniaFact]
    public async Task Playback_TogglesAndStopsWhenSwitchingEntry()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("a_walk.img", frameCount: 20, width: 8, height: 8);
        folder.WriteImg("b_run.img", frameCount: 20, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        Assert.False(vm.IsPlaying);

        vm.TogglePlay();
        Assert.True(vm.IsPlaying);

        vm.TogglePlay();
        Assert.False(vm.IsPlaying);

        // 播放中切换条目 → 必须先停表（否则旧定时器会推一个即将被替换的帧列表）
        vm.TogglePlay();
        Assert.True(vm.IsPlaying);

        vm.SelectedEntry = vm.Entries[1];
        Assert.False(vm.IsPlaying);

        await vm.PendingFrameLoad!;
        Assert.Equal(20, vm.Frames.Count);
    }

    /// <summary>
    ///     8) 状态栏文案：打开后有内容、模式文案正确、保存后提示"已写回"。
    /// </summary>
    [AvaloniaFact]
    public async Task StatusAndModeText_ReflectOpenAndSave()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        Assert.False(string.IsNullOrWhiteSpace(vm.Status));
        Assert.Contains("文件夹", vm.ModeText);
        Assert.True(vm.IsFolderMode);
        Assert.True(vm.CanSave);
        Assert.Contains("写回", vm.SaveTooltip);

        // 文件夹模式的保存 = 原子写回原 .img
        await vm.SaveAsync();
        Assert.Contains("已写回", vm.Status);
        Assert.Contains("walk.img", vm.Status);
    }
}
