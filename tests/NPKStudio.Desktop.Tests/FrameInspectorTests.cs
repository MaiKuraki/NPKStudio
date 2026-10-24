using Avalonia.Headless.XUnit;

using NPKStudio.Desktop.ViewModels;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     帧编辑闭环的 ViewModel 侧断言：增删 / 重排 / 改位置 / 改尺寸 / 多选删除。
///     <para>
///         每条都同时断言<b>卡片列表</b>（<see cref="MainWindowViewModel.Frames" />）与<b>底层 IMG</b>
///         （<see cref="MainWindowViewModel.CurrentImgFile" />）两边，避免出现"界面删了、文件还在"的错位。
///     </para>
///     <para>测试素材由 <see cref="TestFolder" /> 现场生成（v2 + ARGB1555 + ZLIB），不依赖本机路径。</para>
/// </summary>
public sealed class FrameInspectorTests
{
    /// <summary>1) 删除当前帧：帧数与文件同步，选中落到"原位置"那一帧（复用同一个卡片对象）。</summary>
    [AvaloniaFact]
    public async Task DeleteSelectedFrame_RemovesFromFileAndSelectsSamePosition()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 5, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var img = vm.CurrentImgFile!;
        var first = vm.Frames[0];
        var third = vm.Frames[2];
        var fourth = vm.Frames[3];      // 删掉 #002 之后，应该由它顶到原位置

        vm.SelectedFrame = third;
        vm.DeleteSelectedFrames();

        Assert.Equal(4, vm.Frames.Count);
        Assert.Equal(4, img.Frames.Count);
        Assert.DoesNotContain(third.Frame, img.Frames);

        // 选中策略：删完选原位置那一帧
        Assert.Same(fourth, vm.SelectedFrame);
        Assert.Equal(2, vm.SelectedFrame!.Frame.Index);
        Assert.Equal("3 / 4", vm.FrameCounter);

        // 复用：没被删的卡片对象一个都不能换（换掉就等于缩略图要重新解码）
        Assert.Same(first, vm.Frames[0]);
        Assert.Same(fourth, vm.Frames[2]);
    }

    /// <summary>2) 插入帧：插到当前帧之后，新帧是 1×1 且自动选中。</summary>
    [AvaloniaFact]
    public async Task InsertFrameAfterSelected_AddsOnePixelTransparentFrame()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var img = vm.CurrentImgFile!;
        vm.SelectedFrame = vm.Frames[1];
        vm.InsertFrameAfterSelected();

        Assert.Equal(4, vm.Frames.Count);
        Assert.Equal(4, img.Frames.Count);

        var inserted = img.Frames[2];
        Assert.Equal(1, inserted.Width);
        Assert.Equal(1, inserted.Height);
        Assert.Same(inserted, vm.SelectedFrame!.Frame);
        Assert.Equal(2, vm.SelectedFrame!.Frame.Index);
        Assert.Contains("1×1", vm.Status);
    }

    /// <summary>3) 改 X / Y：只动索引表字段，状态栏给出可读反馈。</summary>
    [AvaloniaFact]
    public async Task ChangePosition_WritesFrameFieldsAndStatus()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var frame = vm.Frames[1].Frame;
        vm.SelectedFrame = vm.Frames[1];

        vm.Inspector.X = -12;
        vm.Inspector.Y = 34;

        Assert.Equal(-12, frame.X);
        Assert.Equal(34, frame.Y);
        Assert.False(vm.Inspector.XInvalid);
        Assert.False(vm.Inspector.YInvalid);
        Assert.Contains("位置", vm.Status);
        Assert.Contains("(-12, 34)", vm.Status);
    }

    /// <summary>4) 改宽高：像素被重采样，解码尺寸与新宽高一致。</summary>
    [AvaloniaFact]
    public async Task ChangeSize_ResamplesPixelsAndDecodesAtNewSize()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 2, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var frame = vm.Frames[0].Frame;
        vm.SelectedFrame = vm.Frames[0];

        vm.Inspector.Width = 20;
        vm.Inspector.Height = 16;

        // 宽高提交是防抖的（避免每敲一个字符重采样一次），断言前显式提交
        vm.Inspector.CommitPendingSize();

        Assert.Equal(20, frame.Width);
        Assert.Equal(16, frame.Height);
        Assert.False(vm.Inspector.WidthInvalid);
        Assert.False(vm.Inspector.HeightInvalid);
        Assert.Contains("尺寸", vm.Status);

        var decoded = vm.CurrentImgFile!.Decode(frame);
        Assert.NotNull(decoded);
        Assert.Equal(20, decoded!.Width);
        Assert.Equal(16, decoded.Height);
    }

    /// <summary>5) 多选两帧后删除：两帧一起消失，剩下的卡片对象被复用。</summary>
    [AvaloniaFact]
    public async Task DeleteMultipleSelectedFrames_RemovesAllOfThem()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 6, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var img = vm.CurrentImgFile!;
        var keep = vm.Frames[0];
        var second = vm.Frames[1];
        var third = vm.Frames[2];
        var fourth = vm.Frames[3];

        vm.SelectedFrame = second;
        vm.SelectedFrames.Add(second);
        vm.SelectedFrames.Add(third);

        Assert.Equal(2, vm.SelectedFrames.Count);
        Assert.False(vm.Inspector.IsSingle);
        Assert.Contains("2", vm.Inspector.TitleText);

        vm.DeleteSelectedFrames();

        Assert.Equal(4, vm.Frames.Count);
        Assert.Equal(4, img.Frames.Count);
        Assert.DoesNotContain(second.Frame, img.Frames);
        Assert.DoesNotContain(third.Frame, img.Frames);

        Assert.Same(keep, vm.Frames[0]);
        Assert.Same(fourth, vm.Frames[1]);
        Assert.Same(fourth, vm.SelectedFrame);
        Assert.Equal("2 / 4", vm.FrameCounter);
    }

    /// <summary>6) 边界：删最后一帧选新的末帧；删空则清空选中并隐藏检查器。</summary>
    [AvaloniaFact]
    public async Task DeleteAtBoundaries_FallsBackToLastFrameThenClearsSelection()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        // 首帧：上移按钮应禁用，下移可用
        vm.SelectedFrame = vm.Frames[0];
        Assert.False(vm.Inspector.CanMoveUp);
        Assert.True(vm.Inspector.CanMoveDown);

        vm.SelectedFrame = vm.Frames[^1];
        vm.DeleteSelectedFrames();

        Assert.Equal(2, vm.Frames.Count);
        Assert.Same(vm.Frames[^1], vm.SelectedFrame);
        Assert.Equal(1, vm.SelectedFrame!.Frame.Index);
        Assert.True(vm.Inspector.HasSelection);

        vm.SelectedFrame = vm.Frames[0];
        vm.DeleteSelectedFrames();
        vm.SelectedFrame = vm.Frames[0];
        vm.DeleteSelectedFrames();

        Assert.Empty(vm.Frames);
        Assert.Null(vm.SelectedFrame);
        Assert.False(vm.Inspector.HasSelection);
        Assert.False(vm.Inspector.CanInsert);
        Assert.Equal("0 / 0", vm.FrameCounter);
    }

    /// <summary>7) 切换 IMG：多选不能带到新图上（否则检查器会显示"已选 N 帧"并误删新图的帧）。</summary>
    [AvaloniaFact]
    public async Task SwitchingEntry_ClearsMultiSelection()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("a_walk.img", frameCount: 4, width: 8, height: 8);
        folder.WriteImg("b_run.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.SelectedFrame = vm.Frames[1];
        vm.SelectedFrames.Add(vm.Frames[1]);
        vm.SelectedFrames.Add(vm.Frames[2]);
        Assert.Equal(2, vm.SelectedFrames.Count);

        var previous = vm.Frames.ToList();

        vm.SelectedEntry = vm.Entries[1];
        await vm.PendingFrameLoad!;

        // 上一条 IMG 的卡片一个都不能留在选区里
        foreach (var stale in previous) {
            Assert.DoesNotContain(stale, vm.SelectedFrames);
        }

        // 无控件环境里 SelectedFrames 只会被清空；真实界面上 ListBox 会把首帧重新填进来（见 FrameEditUiTests）
        Assert.True(vm.SelectedFrames.Count <= 1);
        Assert.Same(vm.Frames[0], vm.SelectedFrame);
        Assert.True(vm.Inspector.IsSingle);
    }
}
