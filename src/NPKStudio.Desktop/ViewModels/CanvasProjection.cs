using Avalonia;

using NPKStudio.Core.Formats;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>
///     画布坐标系：整个 IMG 的<b>联合包围盒</b>，以及当前帧在其中的位置。
///     <para>
///         从 <c>MainWindowViewModel</c> 抽出来的（原 <c>UpdateCanvasBounds</c> / <c>UpdateFrameOffset</c>
///         + <c>CanvasSize</c> / <c>FrameOffset</c> 两个属性）。特性：
///         <list type="bullet">
///             <item>只在切换 IMG 时重算包围盒（播放时不跳）；</item>
///             <item>LINK 帧不参与（它宽高为 0、X/Y 为 0，算进去会把包围盒错误地扩到原点）。</item>
///         </list>
///     </para>
///     <para>本类<b>不发通知</b>：属性变化由 <c>MainWindowViewModel</c> 的转发层负责 raise。</para>
/// </summary>
internal sealed class CanvasProjection
{
    /// <summary>整个 IMG 的联合包围盒尺寸（无帧时为 <c>default</c>）。</summary>
    public Size CanvasSize { get; private set; }

    /// <summary>当前帧左上角在画布坐标系中的位置（= 帧坐标 − 包围盒左上角）。</summary>
    public Point FrameOffset { get; private set; }

    /// <summary>画布坐标原点对应的角色坐标（= 包围盒左上角）。标尺与状态栏用。</summary>
    public Point Origin => _origin;

    /// <summary>
    ///     按 IMG 的全部帧重算包围盒。传 <c>null</c> 或空帧列表时归零。
    /// </summary>
    /// <param name="image">当前 IMG。</param>
    /// <returns>包围盒是否发生变化（调用方据此决定要不要 raise）。</returns>
    public bool Reset(ImgFile? image)
    {
        var previousSize = CanvasSize;
        var previousOffset = FrameOffset;
        var previousOrigin = _origin;

        if (image is null || image.Frames.Count == 0)
        {
            _origin = default;
            CanvasSize = default;
            FrameOffset = default;
            return CanvasSize != previousSize || FrameOffset != previousOffset || _origin != previousOrigin;
        }

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var counted = 0;

        foreach (var frame in image.Frames)
        {
            if (frame.Width <= 0 || frame.Height <= 0)
            {
                continue;
            }

            minX = Math.Min(minX, frame.X);
            minY = Math.Min(minY, frame.Y);
            maxX = Math.Max(maxX, frame.X + frame.Width);
            maxY = Math.Max(maxY, frame.Y + frame.Height);
            counted++;
        }

        if (counted == 0)
        {
            _origin = default;
            CanvasSize = default;
            FrameOffset = default;
            return CanvasSize != previousSize || FrameOffset != previousOffset || _origin != previousOrigin;
        }

        _origin = new Point(minX, minY);
        CanvasSize = new Size(Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        return CanvasSize != previousSize || FrameOffset != previousOffset || _origin != previousOrigin;
    }

    /// <summary>
    ///     按当前帧更新 <see cref="FrameOffset" />（没有选中帧时归零）。
    /// </summary>
    /// <param name="frame">当前帧；<c>null</c> 表示清空。</param>
    /// <returns>偏移是否发生变化。</returns>
    public bool UpdateFrameOffset(FrameViewModel? frame)
    {
        var previous = FrameOffset;
        FrameOffset = frame is null
            ? default
            : new Point(frame.Frame.X - _origin.X, frame.Frame.Y - _origin.Y);
        return FrameOffset != previous;
    }

    private Point _origin;
}
