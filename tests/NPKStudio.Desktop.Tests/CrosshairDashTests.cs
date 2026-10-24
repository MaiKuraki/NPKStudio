using Avalonia;

using NPKStudio.Desktop.Controls;
using NPKStudio.Desktop.Controls.Rendering;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     十字线的虚线几何。
///     <para>
///         参考线是 1px 实线、十字线是 1px 虚线（实 4 / 空 4）。抽成纯函数是为了让测试能直接断言
///         "永远 1px 宽、节拍恒为 4" ，而不是依赖无头渲染的像素。
///     </para>
/// </summary>
public sealed class CrosshairDashTests {
    /// <summary>垂直线：每段宽 1px，段高 4px，段与段之间空 4px，且不重叠。</summary>
    [Fact]
    public void VerticalSegments_AreOnePixelWide_WithFourPixelRhythm() {
        var segments = new List<Rect>();
        CrosshairRenderer.BuildCrosshairSegments(true, position: 10, from: 100, to: 200, into: segments);

        Assert.Equal(13, segments.Count);

        foreach (var rect in segments) {
            Assert.Equal(1, rect.Width);
            Assert.Equal(10, rect.X);
            Assert.True(rect.Height > 0 && rect.Height <= 4, $"段高应为 (0,4]，实际 {rect.Height}");
        }

        for (var i = 1; i < segments.Count; i++) {
            var gap = segments[i].Y - (segments[i - 1].Y + segments[i - 1].Height);
            Assert.Equal(4, gap);
        }
    }

    /// <summary>水平线：每段高 1px，段宽 4px。</summary>
    [Fact]
    public void HorizontalSegments_AreOnePixelTall() {
        var segments = new List<Rect>();
        CrosshairRenderer.BuildCrosshairSegments(false, position: 42, from: 0, to: 41, into: segments);

        Assert.Equal(6, segments.Count);

        foreach (var rect in segments) {
            Assert.Equal(1, rect.Height);
            Assert.Equal(42, rect.Y);
            Assert.True(rect.Width > 0 && rect.Width <= 4, $"段宽应为 (0,4]，实际 {rect.Width}");
        }
    }

    /// <summary>跨度为零或负数时不产生任何分段（鼠标停在画布边缘时不该画出东西）。</summary>
    [Theory]
    [InlineData(100, 100)]
    [InlineData(120, 100)]
    public void EmptySpan_ProducesNoSegments(double from, double to) {
        var segments = new List<Rect> { new Rect(1, 2, 3, 4) };
        CrosshairRenderer.BuildCrosshairSegments(true, position: 0, from: from, to: to, into: segments);

        Assert.Empty(segments);
    }

    /// <summary>重复调用必须清空上一轮结果，否则缓冲会越积越多。</summary>
    [Fact]
    public void ReusedBuffer_IsClearedBetweenRenders() {
        var segments = new List<Rect>();
        CrosshairRenderer.BuildCrosshairSegments(true, 0, 0, 80, segments);
        var first = segments.Count;

        CrosshairRenderer.BuildCrosshairSegments(true, 0, 0, 16, segments);

        Assert.True(segments.Count < first);
        Assert.Equal(2, segments.Count);
    }
}
