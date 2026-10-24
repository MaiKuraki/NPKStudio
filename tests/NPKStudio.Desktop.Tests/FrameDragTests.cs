using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using NPKStudio.Desktop.ViewModels;
using NPKStudio.Desktop.Views;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     帧拖拽排序的验收：插入缝纯函数、真实指针拖拽、撤销栈还原。
///     <para>
///         纯函数那几条是"决策逻辑"的唯一真相：给定卡片矩形与指针位置，插到哪个缝必须逐条可断言；
///         真实指针那条证明 <c>PointerPressed/Moved/Released</c> 接线没漏（含 4px 阈值）。
///     </para>
/// </summary>
public sealed class FrameDragTests
{
    /// <summary>一行 4 张卡的矩形（与真实容器同尺寸：96 + 8 右边距 = 104，高 116 + 8 = 124）。</summary>
    private static Rect Card(double left, double top = 0) => new(left, top, 104, 124);

    /// <summary>指针落在各卡片左半 / 右半 → 插入缝逐条对应 <c>索引</c> / <c>索引 + 1</c>。</summary>
    [Fact]
    public void InsertIndex_LeftAndRightHalves_MapToAdjacentSeams()
    {
        Rect[] cards = [Card(0), Card(104), Card(208), Card(312)];

        // 第 1 张：左半 → 0（最前），右半 → 1
        Assert.Equal(0, FrameDropPlanner.InsertIndex(cards, new Point(10, 40)));
        Assert.Equal(1, FrameDropPlanner.InsertIndex(cards, new Point(90, 40)));

        // 第 2 张：左半 → 1，右半 → 2
        Assert.Equal(1, FrameDropPlanner.InsertIndex(cards, new Point(110, 40)));
        Assert.Equal(2, FrameDropPlanner.InsertIndex(cards, new Point(200, 40)));

        // 第 3 张：左半 → 2，右半 → 3
        Assert.Equal(2, FrameDropPlanner.InsertIndex(cards, new Point(210, 40)));
        Assert.Equal(3, FrameDropPlanner.InsertIndex(cards, new Point(300, 40)));

        // 第 4 张：左半 → 3，右半 → 4（末尾）
        Assert.Equal(3, FrameDropPlanner.InsertIndex(cards, new Point(320, 40)));
        Assert.Equal(4, FrameDropPlanner.InsertIndex(cards, new Point(400, 40)));

        // 卡片外：最左 → 0，最右 → 末尾
        Assert.Equal(0, FrameDropPlanner.InsertIndex(cards, new Point(-20, 40)));
        Assert.Equal(4, FrameDropPlanner.InsertIndex(cards, new Point(500, 40)));

        // 两张卡之间的空隙：按最近的一半决定，两种结果都是同一条缝
        Assert.Equal(1, FrameDropPlanner.InsertIndex(cards, new Point(104, 40)));
    }

    /// <summary>换行（WrapPanel 两行）：行内规则不变；指针落在行间空隙时取垂直最近的一行。</summary>
    [Fact]
    public void InsertIndex_WrappedRows_StayRowLocal()
    {
        Rect[] cards = [Card(0, 0), Card(104, 0), Card(0, 124), Card(104, 124)];

        Assert.Equal(2, FrameDropPlanner.InsertIndex(cards, new Point(20, 150)));
        Assert.Equal(3, FrameDropPlanner.InsertIndex(cards, new Point(80, 150)));
        Assert.Equal(3, FrameDropPlanner.InsertIndex(cards, new Point(110, 150)));
        Assert.Equal(4, FrameDropPlanner.InsertIndex(cards, new Point(260, 150)));

        // 指针正好压在两行交界（y = 第一行底边）：算第一行，插到第 2 张右边
        Assert.Equal(2, FrameDropPlanner.InsertIndex(cards, new Point(260, 124)));
        // 越过交界 1px 就落到第二行
        Assert.Equal(4, FrameDropPlanner.InsertIndex(cards, new Point(260, 125)));
        // 整行之外（在第一行上方）→ 按第一行的左右半决定
        Assert.Equal(1, FrameDropPlanner.InsertIndex(cards, new Point(90, -40)));
    }

    /// <summary>没有卡片时返回 0（不会越界）。</summary>
    [Fact]
    public void InsertIndex_NoCards_ReturnsZero() =>
        Assert.Equal(0, FrameDropPlanner.InsertIndex([], new Point(10, 10)));

    /// <summary>程序化移动：顺序正确、选中被移动的帧、Ctrl+Z 一步还原。</summary>
    [AvaloniaFact]
    public async Task MoveFrameTo_ReordersAndUndoRestores()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var moved = vm.Frames[0];
        var other = vm.Frames[1];

        // 插到第 3 张之前（原始下标 3）→ 摘掉自己后落在下标 2
        vm.MoveFrameTo(moved, 3);

        Assert.Equal(4, vm.Frames.Count);
        Assert.Same(moved, vm.Frames[2]);
        Assert.Same(moved, vm.SelectedFrame);
        Assert.True(vm.CanUndo);
        Assert.True(vm.IsDirty);
        Assert.Same(moved.Frame, vm.CurrentImgFile!.Frames[2]);

        vm.UndoEdit();

        Assert.Same(moved, vm.Frames[0]);
        Assert.Same(other, vm.Frames[1]);
        Assert.Equal(0, moved.Frame.Index);
    }

    /// <summary>插入缝等于原位（含"自己右边那条缝"）时不动模型、不写撤销栈，只给一句提示。</summary>
    [AvaloniaFact]
    public async Task MoveFrameTo_SameSeam_ChangesNothing()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenFolderAsync(folder.Path);

        var third = vm.Frames[2];
        vm.MoveFrameTo(third, 2);   // 插到自己前面 = 原位
        Assert.False(vm.CanUndo);
        Assert.False(vm.IsDirty);
        Assert.Same(third, vm.Frames[2]);

        vm.MoveFrameTo(third, 3);   // 插到自己后面 = 原位
        Assert.False(vm.CanUndo);
        Assert.Same(third, vm.Frames[2]);
    }

    /// <summary>
    ///     真实指针：按住第 1 张、移动超过 4px 后拖到第 3 张右半边再松手 → 顺序变成 B C A D，
    ///     松手后选中被移动的帧。
    /// </summary>
    [AvaloniaFact]
    public async Task DraggingCardWithPointer_MovesItAndSelectsIt()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);
        Dispatcher.UIThread.RunJobs();

        var list = window.FindControl<ListBox>("FrameList");
        Assert.NotNull(list);

        var moved = vm.Frames[0];
        var source = list!.ContainerFromIndex(0);
        var target = list.ContainerFromIndex(2);
        Assert.NotNull(source);
        Assert.NotNull(target);

        var start = source!.TranslatePoint(new Point(20, 40), window);
        var overTarget = target!.TranslatePoint(new Point(80, 40), window);   // 第 3 张右半
        Assert.NotNull(start);
        Assert.NotNull(overTarget);

        Drag(window, start!.Value, overTarget!.Value);

        Assert.Same(moved, vm.Frames[2]);
        Assert.Same(moved, vm.SelectedFrame);

        window.Close();
    }

    /// <summary>同一路径下移动不到 4px：仍然只是一次点击，顺序不变。</summary>
    [AvaloniaFact]
    public async Task PressAndTinyMove_DoesNotReorder()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);
        Dispatcher.UIThread.RunJobs();

        var list = window.FindControl<ListBox>("FrameList");
        var first = vm.Frames[0];
        var start = list!.ContainerFromIndex(0)!.TranslatePoint(new Point(20, 40), window);
        Assert.NotNull(start);

        var tiny = new Point(start!.Value.X + 2, start.Value.Y + 2);
        Drag(window, start.Value, tiny);

        Assert.Same(first, vm.Frames[0]);
        Assert.False(vm.CanUndo);

        window.Close();
    }

    /// <summary>
    ///     拖到列表下边缘时自动下滚：60 帧撑出滚动区，指针停在底边 4px 内，
    ///     一次 <c>PointerMoved</c> 滚一档（16px），松手前 <c>Offset.Y</c> 必须变大。
    /// </summary>
    [AvaloniaFact]
    public async Task DraggingAtBottomEdge_AutoScrollsTheList()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 60, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);
        Dispatcher.UIThread.RunJobs();

        var list = window.FindControl<ListBox>("FrameList")!;
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        Assert.NotNull(scroll);
        Assert.True(scroll!.Extent.Height > scroll.Viewport.Height, "60 帧没有撑出滚动区，这个用例就失去意义了");

        var start = list.ContainerFromIndex(0)!.TranslatePoint(new Point(20, 40), window);
        var bottom = list.TranslatePoint(new Point(40, list.Bounds.Height - 4), window);
        Assert.NotNull(start);
        Assert.NotNull(bottom);

        var before = scroll.Offset.Y;

        window.MouseDown(start!.Value, MouseButton.Left);
        window.MouseMove(new Point(start.Value.X + 8, start.Value.Y + 8), RawInputModifiers.LeftMouseButton);
        window.MouseMove(bottom!.Value, RawInputModifiers.LeftMouseButton);
        window.MouseUp(bottom.Value, MouseButton.Left);

        Assert.True(scroll.Offset.Y > before, $"拖到下边缘没有自动滚动：{before} → {scroll.Offset.Y}");

        window.Close();
    }

    /// <summary>按下 → 越过阈值 → 移到目标 → 松手（中间多给几拍移动，等价于用户拖过去）。</summary>
    private static void Drag(MainWindow window, Point start, Point end) {
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 8, start.Y + 8), RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point((start.X + end.X) / 2, end.Y), RawInputModifiers.LeftMouseButton);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
    }
}
