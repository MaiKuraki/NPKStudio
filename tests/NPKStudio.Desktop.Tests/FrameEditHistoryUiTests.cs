using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.ViewModels;

using Avalonia.Headless.XUnit;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     撤销 / 重做与裁剪的<b>界面层</b>接线：模型恢复之外，卡片列表、选中、脏标记、按钮可用性
///     都要跟着回来——这些只在 VM 上能观察，Core 的单测覆盖不到。
/// </summary>
public sealed class FrameEditHistoryUiTests
{
    /// <summary>删除 → 撤销：帧数、选中帧、CanUndo / CanRedo、IsDirty 全部回来。</summary>
    [AvaloniaFact]
    public async Task DeleteThenUndo_RestoresFrameCountSelectionAndButtons()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 5, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        Assert.False(vm.IsDirty);
        Assert.False(vm.CanUndo);

        vm.SelectedFrame = vm.Frames[2];
        vm.DeleteSelectedFrames();

        Assert.Equal(4, vm.Frames.Count);
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanUndo);
        Assert.False(vm.CanRedo);

        var selectedBeforeUndo = vm.SelectedFrame;

        vm.UndoEdit();

        Assert.Equal(5, vm.Frames.Count);
        Assert.Equal("已撤销", vm.Status);
        Assert.True(vm.CanRedo);
        Assert.True(ReferenceEquals(selectedBeforeUndo, vm.SelectedFrame), "撤销后应尽量保持原来选中的那一帧。");
        Assert.Single(vm.SelectedFrames);

        vm.RedoEdit();

        Assert.Equal(4, vm.Frames.Count);
        Assert.Equal("已重做", vm.Status);
        Assert.True(vm.CanUndo);
    }

    /// <summary>插入 → 撤销：帧数回到原值，撤销栈被清空。</summary>
    [AvaloniaFact]
    public async Task InsertThenUndo_RestoresFrameCount()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.InsertFrameAfterSelected();
        Assert.Equal(4, vm.Frames.Count);
        Assert.True(vm.CanUndo);

        vm.UndoEdit();

        Assert.Equal(3, vm.Frames.Count);
        Assert.False(vm.CanUndo);
    }

    /// <summary>移动 → 撤销：卡片顺序按原样恢复（比较底层帧对象的引用顺序）。</summary>
    [AvaloniaFact]
    public async Task MoveThenUndo_RestoresCardOrder()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var original = vm.Frames.Select(item => item.Frame).ToArray();
        vm.SelectedFrame = vm.Frames[0];
        vm.MoveSelectedFrame(1);

        Assert.NotEqual(original[0], vm.Frames[0].Frame);

        vm.UndoEdit();

        Assert.Equal(original, vm.Frames.Select(item => item.Frame).ToArray());
    }

    /// <summary>没有可撤销记录时给出提示，而不是静默无反应。</summary>
    [AvaloniaFact]
    public async Task UndoWithoutHistory_ReportsNothingToUndo()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 2, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.UndoEdit();

        Assert.Equal("没有可撤销的操作", vm.Status);
        Assert.False(vm.CanUndo);
    }

    /// <summary>切换条目必须丢弃上一个 IMG 的历史与脏标记（否则撤销会作用到错误的模型）。</summary>
    [AvaloniaFact]
    public async Task SwitchingEntry_ClearsHistoryAndDirty()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("a_walk.img", frameCount: 3, width: 8, height: 8);
        folder.WriteImg("b_run.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.InsertFrameAfterSelected();
        Assert.True(vm.CanUndo);
        Assert.True(vm.IsDirty);

        vm.SelectedEntry = vm.Entries[1];
        await vm.PendingFrameLoad!;

        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
        Assert.False(vm.IsDirty);
    }

    /// <summary>裁剪：尺寸与坐标按"绝对位置不变"回补，缩略图跟着重建；撤销能还原。</summary>
    [AvaloniaFact]
    public async Task Trim_ShrinksFrameAndUndoRestores()
    {
        using var folder = TestFolder.Create();
        WriteTrimSample(Path.Combine(folder.Path, "trim.img"));

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var frame = vm.SelectedFrame!.Frame;
        Assert.Equal(8, frame.Width);

        vm.TrimSelectedFrames();

        Assert.Equal(2, frame.Width);
        Assert.Equal(2, frame.Height);
        Assert.True(frame.X == 13, $"裁掉左边 3 像素后 X 必须 +3（绝对位置不变）；实际 {frame.X}。");
        Assert.True(frame.Y == 22, $"裁掉上边 2 像素后 Y 必须 +2；实际 {frame.Y}。");
        Assert.Equal(2, vm.Frames[0].Thumbnail?.Width);
        Assert.True(vm.CanUndo);
        Assert.True(vm.IsDirty);

        vm.UndoEdit();

        Assert.Equal(8, frame.Width);
        Assert.Equal(8, frame.Height);
        Assert.Equal(10, frame.X);
        Assert.Equal(20, frame.Y);
    }

    /// <summary>裁剪已经是紧包围盒的帧：只提示、不产生撤销记录、不置脏。</summary>
    [AvaloniaFact]
    public async Task TrimTightFrame_DoesNotRecordEdit()
    {
        using var folder = TestFolder.Create();
        WriteSolidSample(Path.Combine(folder.Path, "solid.img"), 4, 4);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var frame = vm.SelectedFrame!.Frame;
        vm.TrimSelectedFrames();

        Assert.Equal("已是紧包围盒，无需裁剪", vm.Status);
        Assert.Equal(4, frame.Width);
        Assert.False(vm.CanUndo);
        Assert.False(vm.IsDirty);
    }

    /// <summary>保存成功后脏标记归零（文件夹模式写回原 .img）。</summary>
    [AvaloniaFact]
    public async Task SaveSuccess_ClearsDirty()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.InsertFrameAfterSelected();
        Assert.True(vm.IsDirty);

        await vm.SaveAsync();

        Assert.False(vm.IsDirty);
        Assert.Contains("已写回", vm.Status);
    }

    /// <summary>选中 LINK 帧时裁剪按钮必须禁用（LINK 帧没有自己的像素）。</summary>
    [AvaloniaFact]
    public async Task LinkFrame_DisablesTrim()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        vm.Frames[1].Frame.Type = ColorBits.LINK;
        vm.SelectedFrame = vm.Frames[1];

        Assert.False(vm.CanTrim);

        vm.SelectedFrame = vm.Frames[0];
        Assert.True(vm.CanTrim);
    }

    /// <summary>关闭确认框的 XAML 能独立加载（图标 / 样式 / 取词扩展都在这里解析）。</summary>
    [AvaloniaFact]
    public void SaveChangesDialog_Loads()
    {
        var dialog = new Views.Dialogs.SaveChangesDialog();
        Assert.NotNull(dialog);
        dialog.Close();
    }

    /// <summary>写一个全不透明的 v2 IMG（"已经是紧包围盒"的裁剪样本）。</summary>
    private static void WriteSolidSample(string path, int width, int height) {
        var image = ImgFile.Create(ImgVersion.Ver2, Path.GetFileName(path));
        var frame = image.AddFrame();
        frame.Type = ColorBits.ARGB_1555;
        frame.Compress = CompressMode.ZLIB;
        frame.Width = width;
        frame.Height = height;
        frame.FrameWidth = width;
        frame.FrameHeight = height;
        frame.X = 5;
        frame.Y = 5;

        var raw = new byte[width * height * 2];
        for (var offset = 0; offset < raw.Length; offset += 2) {
            const ushort word = 0x8000 | 0x03E0; // 不透明纯绿
            raw[offset] = (byte)(word & 0xFF);
            raw[offset + 1] = (byte)(word >> 8);
        }

        frame.RawData = ZlibCodec.Compress(raw);
        frame.Length = frame.RawData.Length;

        using var stream = File.Create(path);
        image.Write(stream);
    }

    /// <summary>写一个 8×8 帧、内部 (3,2) 起 2×2 不透明的 v2 IMG（裁剪用样本）。</summary>
    private static void WriteTrimSample(string path) {
        var image = ImgFile.Create(ImgVersion.Ver2, Path.GetFileName(path));
        var frame = image.AddFrame();
        frame.Type = ColorBits.ARGB_1555;
        frame.Compress = CompressMode.ZLIB;
        frame.Width = 8;
        frame.Height = 8;
        frame.FrameWidth = 8;
        frame.FrameHeight = 8;
        frame.X = 10;
        frame.Y = 20;

        var raw = new byte[8 * 8 * 2];
        for (var y = 2; y < 4; y++) {
            for (var x = 3; x < 5; x++) {
                var offset = ((y * 8) + x) * 2;
                const ushort word = 0x8000 | 0x7C00; // 不透明纯红
                raw[offset] = (byte)(word & 0xFF);
                raw[offset + 1] = (byte)(word >> 8);
            }
        }

        frame.RawData = ZlibCodec.Compress(raw);
        frame.Length = frame.RawData.Length;

        using var stream = File.Create(path);
        image.Write(stream);
    }
}
