using System.Security.Cryptography;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

using NPKStudio.Desktop.Localization;
using NPKStudio.Desktop.Services;
using NPKStudio.Desktop.ViewModels;
using NPKStudio.Desktop.Views;
using NPKStudio.TestSupport;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     帧可见性（隐藏但不删除）的验收：播放跳过、全隐藏不空转、**不置脏 / 不进撤销栈 / 不影响导出与保存**。
///     <para>
///         这组断言的重点不在"能不能变暗"，而在<b>隐藏只是视图状态</b>：
///         任何一条漏掉（比如误调 <c>Record()</c> 或改了 <see cref="NPKStudio.Core.Formats.ImgFile" />），
///         都会让"调试完关掉文件"变成一次真实的文档修改。
///     </para>
/// </summary>
public sealed class FrameVisibilityTests
{
    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    /// <summary>
    ///     3 帧、隐藏第 2 帧：从第 1 帧前进两次 → 先跳到第 3 帧，第二次因不循环停在原地（位置仍是第 3 帧）。
    ///     <para>第 2 帧全程不会被选中——这正是"播放跳过隐藏帧"。</para>
    /// </summary>
    [AvaloniaFact]
    public async Task Playback_SkipsHiddenFrame_AndStopsAtEndWithoutLoop()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.SelectedFrame = vm.Frames[0];
        vm.Frames[1].IsVisible = false;
        vm.RefreshFrameVisibility();
        vm.LoopPlayback = false;

        vm.TogglePlay();
        Assert.True(vm.IsPlaying);

        // 第 1 次前进：跳过第 2 帧，落在第 3 帧
        vm.AdvanceFrame();
        Assert.Same(vm.Frames[2], vm.SelectedFrame);

        // 第 2 次前进：已在末帧且不循环 → 停表，位置不变（不会落到被隐藏的第 2 帧）
        vm.AdvanceFrame();
        Assert.False(vm.IsPlaying);
        Assert.Same(vm.Frames[2], vm.SelectedFrame);
    }

    /// <summary>循环播放时同样跳过：第 3 帧之后回绕到第 1 帧，隐藏的第 2 帧永远不被选中。</summary>
    [AvaloniaFact]
    public async Task Playback_WithLoop_WrapsWithoutTouchingHiddenFrame()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.SelectedFrame = vm.Frames[0];
        vm.Frames[1].IsVisible = false;
        vm.LoopPlayback = true;

        vm.AdvanceFrame();
        Assert.Same(vm.Frames[2], vm.SelectedFrame);

        vm.AdvanceFrame();
        Assert.Same(vm.Frames[0], vm.SelectedFrame);

        Assert.NotSame(vm.Frames[1], vm.SelectedFrame);
    }

    /// <summary>全部隐藏：一拍就停表、当前帧原地不动、状态栏说明原因（不空转、不崩）。</summary>
    [AvaloniaFact]
    public async Task Playback_AllHidden_StopsInPlaceWithStatus()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.SelectedFrame = vm.Frames[0];
        foreach (var frame in vm.Frames) {
            frame.IsVisible = false;
        }

        vm.RefreshFrameVisibility();
        vm.TogglePlay();
        Assert.True(vm.IsPlaying);

        vm.AdvanceFrame();

        Assert.False(vm.IsPlaying);
        Assert.Same(vm.Frames[0], vm.SelectedFrame);
        Assert.Equal(Loc["status.playbackAllHidden"], vm.Status);
        Assert.Equal(3, vm.HiddenFrameCount);
        Assert.True(vm.HasHiddenFrames);
    }

    /// <summary>
    ///     隐藏不置脏、不进撤销栈：<c>IsDirty</c> / <c>CanUndo</c> 双双保持 false，
    ///     底层帧表一个元素都没动（卡片对象也还是同一个）。
    /// </summary>
    [AvaloniaFact]
    public async Task HidingFrame_DoesNotDirtyDocument_AndDoesNotEnterUndoStack()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var img = vm.CurrentImgFile!;
        var framesBefore = img.Frames.ToArray();
        var card = vm.Frames[1];

        vm.SelectedFrame = card;
        Assert.False(vm.IsDirty);

        vm.HideSelectedFrames();

        Assert.False(card.IsVisible);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
        Assert.Equal(framesBefore, img.Frames.ToArray());
        Assert.Same(card, vm.Frames[1]);
        Assert.Same(card, vm.SelectedFrame);
    }

    /// <summary>当前帧被隐藏时：画布仍显示它（不自动跳走），状态栏明确提示。</summary>
    [AvaloniaFact]
    public async Task HidingCurrentFrame_KeepsCanvasImage_AndExplainsInStatusBar()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var current = vm.Frames[1];
        vm.SelectedFrame = current;
        var imageBefore = vm.CurrentImage;
        Assert.NotNull(imageBefore);

        vm.HideSelectedFrames();

        Assert.Same(current, vm.SelectedFrame);
        Assert.Same(imageBefore, vm.CurrentImage);
        Assert.Equal(Loc["status.currentFrameHidden"], vm.Status);
        Assert.True(vm.IsSelectedFrameHidden);
    }

    /// <summary>
    ///     导出不受影响：隐藏 1 帧后批量导出仍<b>逐帧</b>算出 PNG（3 帧 → 3 张，隐藏帧也有自己的那一张）。
    ///     <para>
    ///         这里断言的是导出计划（解码 + 目标文件名）——无头宿主没有 Skia 写盘能力，
    ///         真正落盘的字节由 <see cref="HidingFrame_DoesNotAffectGifExport" /> 用纯托管编码器补上。
    ///     </para>
    /// </summary>
    [AvaloniaFact]
    public async Task HidingFrame_DoesNotAffectPngBatchExport()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.Frames[1].IsVisible = false;
        vm.RefreshFrameVisibility();

        var output = Path.Combine(folder.Path, "png");
        var items = new List<PngBatchItem>();
        var outcome = FrameExportService.ForEachDecodableFrame(
            vm.CurrentImgFile!, vm.Frames.ToList(), output, "walk", items.Add);

        Assert.Equal(3, items.Count);
        Assert.Equal(0, outcome.Failed);
        Assert.EndsWith("_001.png", items[1].Path);
        // 隐藏帧照样解码出真实像素（不是被跳过、也不是空图）
        Assert.Equal(8, items[1].Pixels.Width);
    }

    /// <summary>
    ///     GIF 导出同样不看可见性：隐藏 1 帧后导出全部 3 帧，文件真的写出来。
    ///     <para>GIF 编码是纯托管的（不依赖渲染后端），所以这条能拿到真实字节。</para>
    /// </summary>
    [AvaloniaFact]
    public async Task HidingFrame_DoesNotAffectGifExport()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.SelectedFrame = vm.Frames[0];
        vm.Frames[1].IsVisible = false;
        vm.RefreshFrameVisibility();

        var path = Path.Combine(folder.Path, "walk.gif");
        await vm.ExportGifAsync(path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
    }

    /// <summary>
    ///     保存字节不变：隐藏前后各写回一次 .img，SHA-256 相同——
    ///     证明隐藏<b>完全没有进入编码链路</b>。
    /// </summary>
    [AvaloniaFact]
    public async Task HidingFrame_DoesNotChangeSavedBytes()
    {
        using var folder = TestFolder.Create();
        var path = folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        await vm.SaveAsync();
        var before = Sha256(path);

        vm.Frames[0].IsVisible = false;
        vm.Frames[2].IsVisible = false;
        vm.RefreshFrameVisibility();

        await vm.SaveAsync();
        var after = Sha256(path);

        Assert.Equal(before, after);
        Assert.False(vm.IsDirty);
    }

    /// <summary>
    ///     NPK 模式：隐藏不会置 <c>NpkEntry.IsModified</c>（否则另存 NPK 会重新序列化该条目，
    ///     变成一个"看起来没改过、实际重写了"的文件）。
    /// </summary>
    [AvaloniaFact]
    public async Task HidingFrames_DoesNotMarkNpkEntryModified()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        var sample = SampleFiles.SyntheticSamplePath;

        var vm = new MainWindowViewModel();
        await vm.OpenAsync(sample);

        // 第一个条目可能是 .ogg / 无帧 IMG：找到有帧的那一个
        foreach (var entry in vm.Entries) {
            vm.SelectedEntry = entry;
            if (vm.PendingFrameLoad is { } load) {
                await load;
            }

            if (vm.Frames.Count > 1) break;
        }

        Assert.True(vm.Frames.Count > 1, "样本里没有找到多帧 IMG");
        var entryItem = vm.SelectedEntry!;
        Assert.NotNull(entryItem.Npk);
        Assert.False(entryItem.Npk!.IsModified);

        vm.SelectedFrame = vm.Frames[0];
        vm.HideSelectedFrames();

        Assert.False(entryItem.Npk!.IsModified);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanUndo);
    }

    /// <summary>右键菜单三个动作：隐藏 / 显示 / 全部显示，并且「全部显示」的启停跟随隐藏数量。</summary>
    [AvaloniaFact]
    public async Task ShowAndShowAll_RestoreVisibility()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        Assert.False(vm.HasHiddenFrames);

        vm.SelectedFrame = vm.Frames[0];
        vm.HideSelectedFrames();
        vm.SelectedFrame = vm.Frames[1];
        vm.HideSelectedFrames();

        Assert.Equal(2, vm.HiddenFrameCount);
        Assert.True(vm.HasHiddenFrames);

        vm.ShowSelectedFrames();
        Assert.Equal(1, vm.HiddenFrameCount);

        vm.ShowAllFrames();
        Assert.Equal(0, vm.HiddenFrameCount);
        Assert.False(vm.HasHiddenFrames);
        Assert.All(vm.Frames, frame => Assert.True(frame.IsVisible));
    }

    /// <summary>多选时作用于整个 <c>SelectedFrames</c>（与删除 / 裁剪同一套作用范围规则）。</summary>
    [AvaloniaFact]
    public async Task HidingMultipleSelection_HidesEverySelectedFrame()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);

        var list = window.FindControl<ListBox>("FrameList")!;
        list.Selection!.Clear();
        list.Selection.Select(1);
        list.Selection.Select(2);
        Assert.Equal(2, vm.SelectedFrames.Count);

        vm.HideSelectedFrames();

        Assert.False(vm.Frames[1].IsVisible);
        Assert.False(vm.Frames[2].IsVisible);
        Assert.True(vm.Frames[0].IsVisible);
        Assert.True(vm.Frames[3].IsVisible);
        Assert.Equal(2, vm.HiddenFrameCount);
        Assert.False(vm.IsDirty);

        window.Close();
    }

    /// <summary>帧计数里带「隐 N」，全部显示后回到纯 <c>x / y</c>。</summary>
    [AvaloniaFact]
    public async Task FrameCounter_AppendsHiddenCountOnlyWhenNeeded()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.SelectedFrame = vm.Frames[0];
        Assert.Equal($"1 / 4", vm.FrameCounter);

        vm.SelectedFrame = vm.Frames[2];
        vm.HideSelectedFrames();

        Assert.Equal($"3 / 4{Loc.F("panel.hiddenSuffix", 1)}", vm.FrameCounter);

        vm.ShowAllFrames();
        Assert.Equal("3 / 4", vm.FrameCounter);
    }

    /// <summary>H 键切换可见性；焦点在 <see cref="TextBox" /> 时必须放行（否则搜索框里打 h 会隐藏帧）。</summary>
    [AvaloniaFact]
    public async Task HKey_TogglesVisibility_ButIsIgnoredFromTextBox()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);
        vm.SelectedFrame = vm.Frames[1];

        window.RaiseEvent(new KeyEventArgs {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.H
        });
        Assert.False(vm.Frames[1].IsVisible);

        // 状态栏的常驻 chip 跟着"当前帧是否隐藏"走（不是一次性提示）
        var chip = window.FindControl<Border>("FrameHiddenChip");
        Assert.NotNull(chip);
        Assert.True(chip!.IsVisible);

        // 搜索框里按 H：事件源是 TextBox，必须原样放行
        var search = window.FindControl<TextBox>("SearchBox");
        Assert.NotNull(search);
        search!.RaiseEvent(new KeyEventArgs {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.H
        });
        Assert.False(vm.Frames[1].IsVisible);

        // 再按一次（焦点不在输入框）→ 恢复显示
        window.RaiseEvent(new KeyEventArgs {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.H
        });
        Assert.True(vm.Frames[1].IsVisible);
        Assert.False(chip.IsVisible);

        window.Close();
    }

    /// <summary>
    ///     帧列表右键菜单：三个动作 + 一条分隔线，文案取词表；<b>「全部显示」只在有隐藏帧时可用</b>。
    ///     <para>
    ///         <c>ContextMenu</c> 不在可视树里，<c>DataContext</c> 不会自动继承——漏了 code-behind 里那句赋值，
    ///         <c>IsEnabled="{Binding HasHiddenFrames}"</c> 会静默失效（永远 false）。这条把接线钉住。
    ///     </para>
    /// </summary>
    [AvaloniaFact]
    public async Task FrameContextMenu_ExposesThreeActions_AndEnablesShowAllOnlyWhenNeeded()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);

        var menu = window.FindControl<ListBox>("FrameList")!.ContextMenu;
        Assert.NotNull(menu);
        Assert.Same(vm, menu!.DataContext);

        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal(Loc["menu.frame.hide"], items[0].Header);
        Assert.Equal(Loc["menu.frame.show"], items[1].Header);
        Assert.Equal(Loc["menu.frame.showAll"], items[2].Header);
        Assert.Single(menu.Items.OfType<Separator>());

        // 启动时没有隐藏帧 → 「全部显示」置灰
        Assert.False(vm.HasHiddenFrames);
        Assert.False(items[2].IsEnabled);

        vm.SelectedFrame = vm.Frames[0];
        vm.HideSelectedFrames();

        Assert.True(vm.HasHiddenFrames);
        Assert.True(items[2].IsEnabled);

        window.Close();
    }

    private static string Sha256(string path) {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
