using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats;

/// <summary>
///     把 BGRA8888 编成"该 IMG 版本 + 该帧格式"下的未压缩像素字节。
///     <para>
///         v1/v2 走 <see cref="ColorCodec" />（1555/4444/8888）；v4/v6 的像素是 1 字节调色板色号，
///         走 <see cref="PaletteIndexCodec" /> 反向量化（色表 ≤256 项，线性扫描就近色）。
///     </para>
///     <para>
///         <b>为什么需要集中这一处</b>：换图、缩放、裁剪三条编辑路径都要重编码像素，
///         如果各自写 <c>ColorCodec.Encode(..., frame.Type)</c>，v4/v6 的 0x0E 类型会被写成 2 字节/像素，
///         文件当场损坏。版本判定只在这里做一次。
///     </para>
/// </summary>
internal static class FramePixelEncoder
{
    /// <summary>该版本是否使用调色板索引像素（v4/v6）。</summary>
    /// <param name="version">IMG 版本。</param>
    /// <returns>v4/v6 返回 <c>true</c>。</returns>
    internal static bool IsPaletteIndexed(ImgVersion version)
    {
        return version is ImgVersion.Ver4 or ImgVersion.Ver6;
    }

    /// <summary>该版本每像素的未压缩字节数（v4/v6 为 1）。</summary>
    /// <param name="version">IMG 版本。</param>
    /// <returns>1 或 2（具体帧若是 8888 由调用方另行处理）。</returns>
    internal static int BytesPerPixel(ImgVersion version)
    {
        return IsPaletteIndexed(version) ? 1 : 2;
    }

    /// <summary>
    ///     把一张 BGRA 位图写成该帧的像素数据——<b>所有编辑路径的唯一出口</b>。
    ///     <para>
    ///         v1/v2/v4/v6：按 <see cref="Encode" /> 编成未压缩字节后 zlib 压缩，写回
    ///         <see cref="ImgFrame.RawData" />；
    ///         v5：帧没有数据区，像素要落进一张纹理页（见 <see cref="WriteVer5Texture" />）。
    ///     </para>
    ///     <para>
    ///         <b>为什么必须集中</b>：换图、缩放、裁剪三条路径原先各自写
    ///         <c>RawData = Zlib(...Encode(...))</c>，v5 走这条路会写出"帧自带像素数据"的非法结构
    ///         （v5 的帧像素只由纹理表 + UV 决定）。
    ///     </para>
    /// </summary>
    /// <param name="file">帧所属的 IMG。</param>
    /// <param name="frame">目标帧（几何字段必须已按新图更新）。</param>
    /// <param name="image">新的像素内容。</param>
    internal static void WritePixels(ImgFile file, ImgFrame frame, RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(image);

        if (file.Version == ImgVersion.Ver5)
        {
            WriteVer5Texture(file, frame, image);
            return;
        }

        frame.RawData = ZlibCodec.Compress(Encode(file, frame, frame.Type, image.Pixels));
        frame.Length = frame.RawData.Length;
        frame.Compress = CompressMode.ZLIB;
    }

    /// <summary>
    ///     v5：把帧的像素写成一张<b>新</b>纹理页，并把帧的 <see cref="ImgFrame.Texture" /> 指向它。
    ///     <para>
    ///         ⚠️ <b>v5 写回不是 DXT 保真，是 1555 重编码</b>：本轮不实现 DXT 编码，纹理一律按
    ///         <see cref="ColorBits.ARGB_1555" /> 存未压缩像素（再 zlib 压缩），与旧实现
    ///         <c>Texture.CreateFromBitmap</c> 的降级口径一致。后果是：读 DXT5 → 写回后体积变大、
    ///         并伴随一次 1555 量化损失（5 位色深 + 1 位 alpha）。
    ///     </para>
    ///     <para>
    ///         旧纹理页<b>不删不改</b>：它可能被别的帧共享（图集裁剪），就地改写会破坏那些帧；
    ///         代价是反复换图会让未被引用的旧页留在文件里（已知遗留缺陷）。
    ///     </para>
    /// </summary>
    /// <param name="file">帧所属的 IMG。</param>
    /// <param name="frame">目标帧。</param>
    /// <param name="image">新的像素内容。</param>
    private static void WriteVer5Texture(ImgFile file, ImgFrame frame, RgbaImage image)
    {
        var pixels = ColorCodec.Encode(image.Pixels, ColorBits.ARGB_1555);
        var texture = new ImgTexture
        {
            // 旧实现 Texture.Version 默认 Dxt1(1)；Type 为未压缩格式时该字段不参与解码，跟着旧实现写 1。
            Version = 1,
            Type = ColorBits.ARGB_1555,
            Index = file.Textures.Count,
            FullLength = pixels.Length,
            Width = image.Width,
            Height = image.Height,
            Data = ZlibCodec.Compress(pixels)
        };
        texture.Length = texture.Data.Length;
        file.AddTexture(texture);

        frame.Texture = new TextureRef
        {
            Unknown = 0,
            TextureIndex = texture.Index,
            Rect = new PixelBounds(0, 0, image.Width, image.Height),
            Rotate = 0
        };

        // 帧侧字段与"纹理帧"口径对齐：type 落 1555、compress 用 0x07、length 恒 0、无帧级数据。
        frame.Type = ColorBits.ARGB_1555;
        frame.Compress = CompressMode.DDS_ZLIB;
        frame.Length = 0;
        frame.RawData = [];
        frame.LinkTarget = null;
        frame.LinkIndex = -1;
    }

    /// <summary>
    ///     编码一帧的未压缩像素字节。
    /// </summary>
    /// <param name="file">帧所属的 IMG（v4/v6 需要色表）。</param>
    /// <param name="frame">目标帧（仅用于异常文案与下标）。</param>
    /// <param name="targetType">目标像素格式（v4/v6 忽略，始终写色号）。</param>
    /// <param name="bgra">BGRA8888 源像素。</param>
    /// <returns>未压缩像素字节（v4/v6 每像素 1 字节）。</returns>
    internal static byte[] Encode(ImgFile file, ImgFrame frame, ColorBits targetType, ReadOnlySpan<byte> bgra)
    {
        if (IsPaletteIndexed(file.Version))
        {
            return PaletteIndexCodec.Encode(bgra, ResolvePalette(file), frame.Index);
        }

        return ColorCodec.Encode(bgra, targetType);
    }

    /// <summary>
    ///     把 1×1 空帧写成"真正的全透明"：优先选色表里 A=0 的项，没有才退回色号 0。
    ///     <para>
    ///         <see cref="FrameLinks" /> 造的空帧是 1555 的 2 字节零值；
    ///         v4/v6 的每像素只有 1 字节，直接沿用会写出长度不符的数据，且色号 0 未必是透明的。
    ///     </para>
    /// </summary>
    /// <param name="file">帧所属的 IMG。</param>
    /// <param name="frame">目标帧（已是 1×1）。</param>
    internal static void FillBlankFrame(ImgFile file, ImgFrame frame)
    {
        var palette = ResolvePalette(file);
        var index = 0;
        for (var i = 0; i < palette.Count; i++)
        {
            if (palette[i].A == 0)
            {
                index = i;
                break;
            }
        }

        var raw = ZlibCodec.Compress(new[] { (byte)index });
        frame.RawData = raw;
        frame.Length = raw.Length;
        frame.Compress = CompressMode.ZLIB;
    }

    /// <summary>
    ///     v4/v6 解码用的色表：v4 用 <see cref="ImgFile.Palette" />；
    ///     v6 用第 0 张表（格式里没有"帧 → 表"的下标字段，旧实现同样以文件级当前表为准）。
    /// </summary>
    /// <param name="file">源文件。</param>
    /// <returns>色表。</returns>
    private static IReadOnlyList<Color32> ResolvePalette(ImgFile file)
    {
        if (file.Version == ImgVersion.Ver6 && file.Palettes.Count > 0)
        {
            return file.Palettes[0];
        }

        return file.Palette;
    }
}
