using Avalonia.Headless.XUnit;

using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.ViewModels;
using NPKStudio.Desktop.ViewModels.Channels;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     <b>多通道就绪的证据</b>：两个 <see cref="FrameChannel" /> 各自打开不同 IMG 时，
///     帧数据、播放进度、选中 / 搜索 / 隐藏状态、变更基线全部互不可见。
///     <para>
///         这些用例<b>不建窗口、不碰任何控件</b>，直接驱动 <see cref="FrameChannel" /> 与
///         <see cref="ChannelCollection" />——如果模型层还残留"同一时刻只能有一个 IMG / 一套播放"的假设，
///         这里就会挂。
///     </para>
///     <para>
///         用 <c>[AvaloniaFact]</c> 只是因为加载帧会走 <c>Dispatcher.UIThread.Post</c> 出缩略图
///         （无头宿主里 UI 线程是真的），并不涉及任何一个可见控件。
///     </para>
/// </summary>
public sealed class ChannelIndependenceTests
{
    /// <summary>
    ///     1) 两个通道各开各的 IMG：帧数、帧尺寸、文件名、底层模型互不干扰。
    /// </summary>
    [AvaloniaFact]
    public async Task TwoChannels_OpenDifferentImages_WithoutInterference()
    {
        using var folder = TestFolder.Create();
        var walk = folder.WriteImg("a_walk.img", frameCount: 12, width: 8, height: 8);
        var run = folder.WriteImg("b_run.img", frameCount: 20, width: 16, height: 16);

        var collection = new ChannelCollection();
        var a = collection.Channels[0];
        var b = collection.Add();

        Assert.Equal(2, collection.Count);

        await a.OpenAsync(walk);
        await b.OpenAsync(run);

        // 帧数
        Assert.Equal(12, a.Frames.Count);
        Assert.Equal(20, b.Frames.Count);

        // 帧尺寸（来自各自的 IMG，不是共享的一份）
        Assert.Equal(8, a.Frames[0].Frame.Width);
        Assert.Equal(8, a.Frames[0].Frame.Height);
        Assert.Equal(16, b.Frames[0].Frame.Width);
        Assert.Equal(16, b.Frames[0].Frame.Height);

        // 文件名
        Assert.Equal("a_walk", a.FileName);   // 单文件模式的文件名不带扩展名
        Assert.Equal("b_run", b.FileName);

        // 底层模型是两个不同的实例
        Assert.NotNull(a.CurrentImgFile);
        Assert.NotNull(b.CurrentImgFile);
        Assert.NotSame(a.CurrentImgFile, b.CurrentImgFile);

        // 画布包围盒也跟着各自的帧表走（X 从 0 递增，所以宽度 = 帧宽 + 最大 X）
        Assert.Equal(8 + 11, a.CanvasSize.Width);
        Assert.Equal(16 + 19, b.CanvasSize.Width);
    }

    /// <summary>
    ///     2) 两个通道独立播放：A 在播（1× = 8 fps）、B 停着；
    ///     推进 A 的帧不会带动 B 的当前帧。
    /// </summary>
    [AvaloniaFact]
    public async Task TwoChannels_PlayIndependently()
    {
        using var folder = TestFolder.Create();
        var walk = folder.WriteImg("a_walk.img", frameCount: 12, width: 8, height: 8);
        var run = folder.WriteImg("b_run.img", frameCount: 12, width: 8, height: 8);

        var collection = new ChannelCollection();
        var a = collection.Channels[0];
        var b = collection.Add();

        await a.OpenAsync(walk);
        await b.OpenAsync(run);

        // 倍率是"观看习惯"，显式钉到 1×（其它用例会改它并落盘）
        a.RateIndex = 3;
        Assert.Equal(8, a.EffectiveFps);

        a.TogglePlay();
        try
        {
            Assert.True(a.IsPlaying);
            Assert.False(b.IsPlaying);

            var aBefore = a.CurrentIndex;
            var bBefore = b.CurrentIndex;

            a.AdvanceFrame();
            a.AdvanceFrame();

            Assert.Equal(aBefore + 2, a.CurrentIndex);   // A 自己走了两拍
            Assert.Equal(bBefore, b.CurrentIndex);       // B 一动没动
            Assert.False(b.IsPlaying);
        }
        finally
        {
            // 别把真实定时器留在别的用例里
            a.Stop();
        }
    }

    /// <summary>
    ///     3) 切活动通道不串味：A 上的选中帧、搜索词、隐藏状态，切到 B 再切回 A 原样还在；
    ///     同时 B 自己的这三样必须是"干净的"。
    /// </summary>
    [AvaloniaFact]
    public async Task SwitchingActiveChannel_KeepsEachChannelStateIntact()
    {
        using var folder = TestFolder.Create();
        var walk = folder.WriteImg("a_walk.img", frameCount: 8, width: 8, height: 8);
        var run = folder.WriteImg("b_run.img", frameCount: 8, width: 8, height: 8);

        var collection = new ChannelCollection();
        var a = collection.Channels[0];
        var b = collection.Add();

        await a.OpenAsync(walk);
        await b.OpenAsync(run);

        // A：选第 4 帧、过滤词 #003、把第 3 帧藏起来
        a.SelectedFrame = a.Frames[3];
        a.FilterText = "#003";
        a.Frames[2].IsVisible = false;
        a.RefreshFrameVisibility();

        Assert.Single(a.FilteredFrames);
        Assert.Equal(1, a.HiddenFrameCount);

        // 切到 B：活动通道变了，B 的状态是自己的（全新、没有任何 A 的痕迹）
        collection.SetActive(1);
        Assert.Same(b, collection.ActiveChannel);
        Assert.Equal(string.Empty, b.FilterText);
        Assert.Equal(a.Frames.Count, b.FilteredFrames.Count);
        Assert.Equal(0, b.HiddenFrameCount);
        Assert.True(b.Frames[2].IsVisible);
        Assert.NotSame(a.SelectedFrame, b.SelectedFrame);

        // 切回 A：三样状态原样保持
        collection.SetActive(0);
        Assert.Same(a, collection.ActiveChannel);
        Assert.Same(a.Frames[3], a.SelectedFrame);
        Assert.Equal("#003", a.FilterText);
        Assert.Single(a.FilteredFrames);
        Assert.Same(a.Frames[3], a.FilteredFrames[0]);
        Assert.False(a.Frames[2].IsVisible);
        Assert.Equal(1, a.HiddenFrameCount);
        Assert.True(b.Frames[2].IsVisible);          // B 的帧仍然可见
    }

    /// <summary>
    ///     4) 变更标记按通道独立：A 里替换一帧后 A 的计数 +1，B 的基线完全不受影响。
    /// </summary>
    [AvaloniaFact]
    public async Task ChangeMarkers_AreTrackedPerChannel()
    {
        using var folder = TestFolder.Create();
        var walk = folder.WriteImg("a_walk.img", frameCount: 5, width: 8, height: 8);
        var run = folder.WriteImg("b_run.img", frameCount: 5, width: 8, height: 8);

        var collection = new ChannelCollection();
        var a = collection.Channels[0];
        var b = collection.Add();

        await a.OpenAsync(walk);
        await b.OpenAsync(run);

        Assert.Equal(0, a.ChangedFrameCount);
        Assert.Equal(0, b.ChangedFrameCount);

        // A 里换掉第 2 帧的素材（走 Core，绕开需要图片解码器的文件选择框）
        var image = a.CurrentImgFile!;
        image.ReplaceFrameImage(a.Frames[1].Frame, Solid(8, 8), ColorBits.UNKNOWN);
        a.RefreshChangeMarkers();

        Assert.Equal(1, a.ChangedFrameCount);
        Assert.True(a.Frames[1].ChangeKind.HasFlag(FrameChangeKind.Pixels));

        // B 的基线与标记一条都没动
        Assert.Equal(0, b.ChangedFrameCount);
        Assert.All(b.Frames, frame => Assert.Equal(FrameChangeKind.None, frame.ChangeKind));
    }

    /// <summary>
    ///     5) 外壳也跟着换：切活动通道后转发属性指向新通道，切回来数据还在。
    ///     <para>补这一条是因为真实界面读的是 <see cref="MainWindowViewModel" />，不是通道本身。</para>
    /// </summary>
    [AvaloniaFact]
    public async Task Shell_FollowsActiveChannel_AndRestoresItOnSwitchBack()
    {
        using var folder = TestFolder.Create();
        var walk = folder.WriteImg("a_walk.img", frameCount: 6, width: 8, height: 8);

        var vm = new MainWindowViewModel();
        await vm.OpenAsync(walk);

        Assert.Equal(6, vm.Frames.Count);
        Assert.Contains("a_walk", vm.FileName);

        var second = vm.Channels.Add();
        Assert.Equal(2, vm.Channels.Count);

        vm.Channels.SetActive(1);
        Assert.Same(second, vm.ActiveChannel);
        Assert.Empty(vm.Frames);
        Assert.Equal(0, vm.ChangedFrameCount);

        vm.Channels.SetActive(0);
        Assert.Equal(6, vm.Frames.Count);
        Assert.Contains("a_walk", vm.FileName);
        Assert.NotNull(vm.SelectedFrame);
    }

    /// <summary>一张纯色图（Core 侧换图素材，不需要任何图片编解码器）。</summary>
    private static RgbaImage Solid(int width, int height)
    {
        var image = new RgbaImage(width, height);
        for (var i = 0; i < image.Pixels.Length; i += 4)
        {
            image.Pixels[i] = 0x20;
            image.Pixels[i + 1] = 0x60;
            image.Pixels[i + 2] = 0xC0;
            image.Pixels[i + 3] = 0xFF;
        }

        return image;
    }
}
