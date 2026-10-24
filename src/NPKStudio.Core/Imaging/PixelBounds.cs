namespace NPKStudio.Core.Imaging;

/// <summary>
///     描述图像平面上的一个矩形区域（整数坐标，单位为像素）。
///     <para>
///         <see cref="X" />/<see cref="Y" /> 是矩形左上角在角色坐标系中的偏移，允许为负；
///         <see cref="Width" />/<see cref="Height" /> 恒为非负数。
///     </para>
/// </summary>
/// <param name="X">矩形左上角横坐标（允许为负）。</param>
/// <param name="Y">矩形左上角纵坐标（允许为负）。</param>
/// <param name="Width">矩形宽度（非负）。</param>
/// <param name="Height">矩形高度（非负）。</param>
public readonly record struct PixelBounds(int X, int Y, int Width, int Height)
{
    /// <summary>
    ///     获取一个空矩形（全部字段为 0）。撤销/裁剪操作在"没有任何不透明像素"时使用它。
    /// </summary>
    public static PixelBounds Empty => default;

    /// <summary>
    ///     右边界（不含），即 <see cref="X" /> + <see cref="Width" />。
    /// </summary>
    public int Right => X + Width;

    /// <summary>
    ///     下边界（不含），即 <see cref="Y" /> + <see cref="Height" />。
    /// </summary>
    public int Bottom => Y + Height;

    /// <summary>
    ///     是否为空矩形（宽或高不为正）。
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    ///     返回可读文本，便于日志与断言失败时定位。
    /// </summary>
    public override string ToString()
    {
        return $"PixelBounds(X={X}, Y={Y}, Width={Width}, Height={Height})";
    }
}
