using System.Runtime.CompilerServices;

namespace NPKStudio.Core.Imaging;

/// <summary>
///     内存位图：内部像素恒为 <b>BGRA8888 + 直通（非预乘）alpha</b>，行优先，
///     <c>stride == Width * 4</c>，像素寻址 <c>(y * Width + x) * 4</c>。
///     <para>
///         <see cref="Pixels" /> 直接暴露底层数组以避免拷贝（性能考虑），
///         但<b>任何方法都不会改变数组长度</b>，也不存在"换一个数组"的路径。
///     </para>
/// </summary>
public sealed class RgbaImage
{
    private const int BytesPerPixel = 4;

    private readonly byte[] _pixels;

    /// <summary>
    ///     创建全透明图像。
    /// </summary>
    /// <param name="width">宽度（&gt; 0）。</param>
    /// <param name="height">高度（&gt; 0）。</param>
    /// <exception cref="ArgumentOutOfRangeException">宽或高不为正。</exception>
    public RgbaImage(int width, int height)
    {
        ValidateSize(width, height);
        Width = width;
        Height = height;
        _pixels = new byte[checked(width * height * BytesPerPixel)];
    }

    private RgbaImage(int width, int height, byte[] pixels)
    {
        ValidateSize(width, height);
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    /// <summary>
    ///     图像宽度（像素）。
    /// </summary>
    public int Width { get; }

    /// <summary>
    ///     图像高度（像素）。
    /// </summary>
    public int Height { get; }

    /// <summary>
    ///     像素缓冲，长度恒为 <c>Width * Height * 4</c>，字节序为 B,G,R,A。
    /// </summary>
    public byte[] Pixels => _pixels;

    /// <summary>
    ///     行跨度（字节），恒为 <c>Width * 4</c>。
    /// </summary>
    public int Stride => Width * BytesPerPixel;

    /// <summary>
    ///     用给定的 BGRA 字节数组接管构造图像（<b>不拷贝</b>，调用方交出所有权）。
    /// </summary>
    /// <param name="pixels">BGRA 字节数组，长度必须等于 <c>width * height * 4</c>。</param>
    /// <param name="width">宽度（&gt; 0）。</param>
    /// <param name="height">高度（&gt; 0）。</param>
    /// <returns>新建的 <see cref="RgbaImage" />。</returns>
    /// <exception cref="ArgumentException">字节数组长度与宽高不匹配。</exception>
    /// <exception cref="ArgumentOutOfRangeException">宽或高不为正。</exception>
    public static RgbaImage FromBgra(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ValidateSize(width, height);
        var expected = checked(width * height * BytesPerPixel);
        if (pixels.Length != expected)
        {
            throw new ArgumentException($"字节数组长度 {pixels.Length} 与 {width}x{height} 期望的 {expected} 不一致。", nameof(pixels));
        }

        return new RgbaImage(width, height, pixels);
    }

    /// <summary>
    ///     用颜色数组构造图像，颜色按行优先顺序排列。
    /// </summary>
    /// <param name="colors">颜色数组，长度必须等于 <c>width * height</c>。</param>
    /// <param name="width">宽度（&gt; 0）。</param>
    /// <param name="height">高度（&gt; 0）。</param>
    /// <returns>新建的 <see cref="RgbaImage" />。</returns>
    /// <exception cref="ArgumentException">颜色数量与宽高不匹配。</exception>
    /// <exception cref="ArgumentOutOfRangeException">宽或高不为正。</exception>
    public static RgbaImage FromColors(Color32[] colors, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(colors);
        ValidateSize(width, height);
        var expected = checked(width * height);
        if (colors.Length != expected)
        {
            throw new ArgumentException($"颜色数量 {colors.Length} 与 {width}x{height} 期望的 {expected} 不一致。", nameof(colors));
        }

        var pixels = new byte[expected * BytesPerPixel];
        for (var i = 0; i < colors.Length; i++)
        {
            var color = colors[i];
            var offset = i * BytesPerPixel;
            pixels[offset] = color.B;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R;
            pixels[offset + 3] = color.A;
        }

        return new RgbaImage(width, height, pixels);
    }

    /// <summary>
    ///     深拷贝一份图像。
    /// </summary>
    /// <returns>与当前实例像素内容完全相同的新实例。</returns>
    public RgbaImage Clone()
    {
        return new RgbaImage(Width, Height, (byte[])_pixels.Clone());
    }

    /// <summary>
    ///     按矩形裁剪，越界部分自动裁到与原图的交集。
    ///     <para>交集为空时返回 1x1 全透明图像（不抛异常、不返回 0 尺寸，避免下游除零）。</para>
    /// </summary>
    /// <param name="bounds">裁剪矩形（允许负坐标与越界尺寸）。</param>
    /// <returns>裁剪后的新图像。</returns>
    public RgbaImage Crop(PixelBounds bounds)
    {
        // 用 long 计算边界，避免 X + Width 溢出。
        var left = Math.Max(0L, bounds.X);
        var top = Math.Max(0L, bounds.Y);
        var right = Math.Min(Width, (long)bounds.X + bounds.Width);
        var bottom = Math.Min(Height, (long)bounds.Y + bounds.Height);

        if (right <= left || bottom <= top)
        {
            // 交集为空：D-03 的教训是"绝不返回非法尺寸"，这里退化为 1x1 全透明。
            return new RgbaImage(1, 1);
        }

        var cropWidth = (int)(right - left);
        var cropHeight = (int)(bottom - top);
        var result = new byte[checked(cropWidth * cropHeight * BytesPerPixel)];
        var sourceStride = Stride;
        var targetStride = cropWidth * BytesPerPixel;

        for (var row = 0; row < cropHeight; row++)
        {
            var sourceOffset = (int)((top + row) * sourceStride + left * BytesPerPixel);
            Buffer.BlockCopy(_pixels, sourceOffset, result, row * targetStride, targetStride);
        }

        return new RgbaImage(cropWidth, cropHeight, result);
    }

    /// <summary>
    ///     缩放图像（最近邻采样，仅用于 GIF 尺寸归一，不做高质量重采样）。
    /// </summary>
    /// <param name="width">目标宽度（&gt; 0）。</param>
    /// <param name="height">目标高度（&gt; 0）。</param>
    /// <returns>缩放后的新图像；尺寸相同时返回深拷贝。</returns>
    /// <exception cref="ArgumentOutOfRangeException">宽或高不为正。</exception>
    public RgbaImage Resize(int width, int height)
    {
        ValidateSize(width, height);
        if (width == Width && height == Height)
        {
            return Clone();
        }

        var result = new byte[checked(width * height * BytesPerPixel)];
        var sourceStride = Stride;
        var targetStride = width * BytesPerPixel;

        // 列映射只算一次。若写在内层循环里，每个像素要付两次 64 位除法（x64 上一次约 20~40 周期）。
        var columnMap = new int[width];
        for (var x = 0; x < width; x++)
        {
            columnMap[x] = (int)((long)x * Width / width);
        }

        for (var y = 0; y < height; y++)
        {
            var sourceRow = (int)((long)y * Height / height) * sourceStride;
            var targetRow = y * targetStride;

            if (width == Width)
            {
                // 同宽时只做行搬运：整行一次拷贝，不必逐像素。
                Buffer.BlockCopy(_pixels, sourceRow, result, targetRow, targetStride);
                continue;
            }

            for (var x = 0; x < width; x++)
            {
                var sourceOffset = sourceRow + columnMap[x] * BytesPerPixel;
                var targetOffset = targetRow + x * BytesPerPixel;
                result[targetOffset] = _pixels[sourceOffset];
                result[targetOffset + 1] = _pixels[sourceOffset + 1];
                result[targetOffset + 2] = _pixels[sourceOffset + 2];
                result[targetOffset + 3] = _pixels[sourceOffset + 3];
            }
        }

        return new RgbaImage(width, height, result);
    }

    /// <summary>
    ///     扫描不透明像素的外接矩形（等价于 <c>ScanBounds(1, out bounds)</c>，即"alpha &gt; 0 才算不透明"）。
    /// </summary>
    /// <param name="bounds">命中的外接矩形；全透明时为 <c>default</c>。</param>
    /// <returns>存在不透明像素返回 <c>true</c>，全透明返回 <c>false</c>（D-03：绝不产生非法矩形）。</returns>
    public bool ScanBounds(out PixelBounds bounds)
    {
        return ScanBounds(1, out bounds);
    }

    /// <summary>
    ///     扫描 alpha 不小于 <paramref name="alphaThreshold" /> 的像素的外接矩形。
    ///     <para>全透明（没有像素达标）时返回 <c>false</c> 且 <paramref name="bounds" /> 为 <c>default</c>；
    ///     返回的宽高恒为正且不超过图像尺寸。</para>
    /// </summary>
    /// <param name="alphaThreshold">alpha 阈值，取值 0~255；0 表示"所有像素都算不透明"。</param>
    /// <param name="bounds">命中的外接矩形；没有像素达标时为 <c>default</c>。</param>
    /// <returns>存在达标像素返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    public bool ScanBounds(byte alphaThreshold, out PixelBounds bounds)
    {
        var minX = Width;
        var minY = Height;
        var maxX = -1;
        var maxY = -1;
        var stride = Stride;

        for (var y = 0; y < Height; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < Width; x++)
            {
                if (_pixels[rowOffset + x * BytesPerPixel + 3] < alphaThreshold)
                {
                    continue;
                }

                if (x < minX)
                {
                    minX = x;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }
        }

        if (maxX < minX || maxY < minY)
        {
            // 全透明：不产生任何矩形，交由调用方提示并跳过（见 TrimFrameCommand 约定）。
            bounds = default;
            return false;
        }

        bounds = new PixelBounds(minX, minY, maxX - minX + 1, maxY - minY + 1);
        return true;
    }

    /// <summary>
    ///     导出为 BGRA 字节数组副本（供 <c>WriteableBitmap</c> 逐行拷贝或跨层传输）。
    /// </summary>
    /// <returns>长度为 <c>Width * Height * 4</c> 的新数组。</returns>
    public byte[] ToBgra32()
    {
        return (byte[])_pixels.Clone();
    }

    /// <summary>
    ///     把第 <paramref name="y" /> 行像素拷贝到目标跨度。<b>不改变</b>本实例的像素数组。
    /// </summary>
    /// <param name="y">行号，必须在 <c>[0, Height)</c> 内。</param>
    /// <param name="dest">目标跨度，长度至少为 <c>Width * 4</c>。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y" /> 越界。</exception>
    /// <exception cref="ArgumentException"><paramref name="dest" /> 长度不足。</exception>
    public void CopyRowTo(int y, Span<byte> dest)
    {
        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, $"行号必须在 [0, {Height}) 范围内。");
        }

        var stride = Stride;
        if (dest.Length < stride)
        {
            throw new ArgumentException($"目标跨度长度 {dest.Length} 小于行跨度 {stride}。", nameof(dest));
        }

        _pixels.AsSpan(y * stride, stride).CopyTo(dest);
    }

    /// <summary>
    ///     取一整行的可写跨度，供解码器批量落像素（免掉每像素一次越界检查与方法调用）。
    ///     <para>寻址仍由本类负责：跨度长度恒为 <c>Width * 4</c>，调用方不得改变数组长度。</para>
    /// </summary>
    /// <param name="y">行号，必须在 <c>[0, Height)</c> 内。</param>
    /// <returns>该行的像素跨度（BGRA，长度 <c>Width * 4</c>）。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y" /> 越界。</exception>
    public Span<byte> GetRowSpan(int y)
    {
        if ((uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, $"行号必须在 [0, {Height}) 范围内。");
        }

        return _pixels.AsSpan(y * Stride, Stride);
    }

    /// <summary>
    ///     读取指定坐标的颜色（越界返回 <see cref="Color32.Transparent" />）。
    /// </summary>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    /// <returns>该坐标的像素颜色。</returns>
    public Color32 GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return Color32.Transparent;
        }

        var offset = (y * Width + x) * BytesPerPixel;
        return Color32.FromBgra(_pixels[offset], _pixels[offset + 1], _pixels[offset + 2], _pixels[offset + 3]);
    }

    /// <summary>
    ///     写入指定坐标的颜色（越界静默忽略；<b>不改变</b>像素数组长度）。
    /// </summary>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    /// <param name="color">要写入的颜色。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPixel(int x, int y, Color32 color)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return;
        }

        var offset = (y * Width + x) * BytesPerPixel;
        _pixels[offset] = color.B;
        _pixels[offset + 1] = color.G;
        _pixels[offset + 2] = color.R;
        _pixels[offset + 3] = color.A;
    }

    /// <summary>
    ///     返回可读文本，便于日志与断言失败时定位。
    /// </summary>
    public override string ToString()
    {
        return $"RgbaImage({Width}x{Height}, {_pixels.Length} bytes)";
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateSize(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "宽度必须为正数。");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "高度必须为正数。");
        }
    }
}
