using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats.Handlers;

/// <summary>
///     v5（<c>"Neople Img File"</c> + DDS 纹理图集）处理器。
///     <para>
///         依据是旧实现 <c>ExtractorSharp.Core/Handle/FifthHandler.cs</c> 的
///         <c>CreateFromStream</c> / <c>AdjustData</c>（<b>当前没有 v5 真实样本</b>可逐字节核对：
///         仓库自带的合成样本全由 v2 构成，测试里的 v5 数据都是按布局手工构造的）。
///     </para>
///     <para>
///         布局（偏移从 IMG 起点算，公共头占 0..31）：
///         <code>
/// 32  : int32 textureCount
/// 36  : int32 totalLength      // 旧实现写 Album.Length（data.Length + 40），语义不可考，读侧忽略
/// 40  : int32 paletteCount
/// 44  : paletteCount × 4 字节（R,G,B,A）
///      : textureCount × 28 字节纹理表项：version, type, index, length, fullLength, width, height
///      : 帧索引区（IndexLength 字节）
///          type + [compress, w, h, length, x, y, fw, fh]（36）
///          纹理帧额外 28 字节：unknown, textureIndex, leftUpX, leftUpY, rightDownX, rightDownY, rotate
///          LINK 帧只有 type + targetIndex（8）
///      : 纹理数据区（textureCount 个 length 字节块，按纹理表顺序）
///      : 内联帧数据区（非 LINK 且非纹理帧，按帧顺序各 length 字节）
/// </code>
///     </para>
///     <para>
///         <b>帧怎样算"纹理帧"</b>：旧实现的判别是 <c>type &lt; LINK &amp;&amp; length != 0</c> → 内联 v2 帧，
///         其余一律读 28 字节 UV 扩展。本类逐字照搬（DXT 类型即便 length != 0 也走纹理分支）。
///     </para>
///     <para>
///         ⚠️ <b>写回不是 DXT 保真</b>：本轮<b>不实现 DXT 编码</b>，每帧都按 ARGB1555 重编码成一张新纹理页
///         （旧实现 <c>Texture.CreateFromBitmap</c> 也是降级成未压缩格式再 zlib 压缩，但没有做"DDS 头 + DXT 块"）。
///         所以"读 DXT5 → 写回"后体积会变大、并有一次 1555 量化损失；要保真必须补 DXT 编码器。
///     </para>
/// </summary>
internal sealed class Ver5Handler : ImgHandlerBase
{
    /// <summary>纹理表项字节数（7 × int32）。</summary>
    internal const int TextureEntrySize = 28;

    /// <summary>纹理帧在帧索引项之后的 UV 扩展字节数（7 × int32）。</summary>
    internal const int TextureUvSize = 28;

    /// <summary>纹理页数量上限。<b>护栏，不是格式限制</b>：拦截损坏文件声明的巨量纹理表。</summary>
    private const int MaxTextures = 4096;

    /// <summary>v5 色表项数上限。<b>护栏，不是格式限制</b>：v5 色表不参与解码，仅原样保留。</summary>
    private const int MaxV5PaletteEntries = 4096;

    /// <inheritdoc />
    public override ImgVersion Version => ImgVersion.Ver5;

    /// <inheritdoc />
    public override void ReadIndex(Stream stream, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(file);

        var textureCount = stream.ReadInt32();
        if (textureCount < 0 || textureCount > MaxTextures)
        {
            throw new ImgFormatException($"v5 纹理数量非法：{textureCount}（允许 0..{MaxTextures}）。");
        }

        // 旧实现把 Album.Length 写在这里（data.Length + 40），但没有读者使用；原样保存，写出时回放。
        file.V5DeclaredLength = stream.ReadInt32();

        var paletteCount = stream.ReadInt32();

        // v5 的色表不参与纹理解码（像素来自纹理页），只是原样保留；因此上限放宽，避免误拒真机文件。
        file.SetPalette(PaletteIndexCodec.ReadColors(stream, paletteCount, MaxV5PaletteEntries));

        for (var i = 0; i < textureCount; i++)
        {
            var texture = new ImgTexture
            {
                Version = stream.ReadInt32(),
                Type = (ColorBits)stream.ReadInt32(),
                Index = stream.ReadInt32(),
                Length = stream.ReadInt32(),
                FullLength = stream.ReadInt32(),
                Width = stream.ReadInt32(),
                Height = stream.ReadInt32()
            };

            ValidateTexture(texture, i);
            file.AddTexture(texture);
        }

        ReadFrames(stream, file);
        ReadPayload(stream, file);
        ResolveLinks(file);
    }

    /// <inheritdoc />
    public override void WriteIndex(BinaryWriter writer, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(file);

        file.IndexLength = ComputeFrameIndexLength(file);
        WriteCommonHeader(writer, file);

        writer.Write(file.Textures.Count);

        // 语义不可考：读到的原值原样回放；新建文件（没有原值）才按真实总长计算。
        writer.Write(file.V5DeclaredLength ?? ComputeTotalLength(file));

        writer.Write(file.Palette.Count);
        PaletteIndexCodec.WriteColors(writer, file.Palette);

        foreach (var texture in file.Textures)
        {
            writer.Write(texture.Version);
            writer.Write((int)texture.Type);
            writer.Write(texture.Index);
            writer.Write(texture.Data.Length);
            writer.Write(texture.FullLength);
            writer.Write(texture.Width);
            writer.Write(texture.Height);
        }

        WriteFrames(writer, file);
    }

    /// <inheritdoc />
    public override void WritePayload(BinaryWriter writer, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(file);

        // 数据区顺序：先所有纹理页，再所有内联帧（与旧实现 AdjustData 完全一致）。
        foreach (var texture in file.Textures)
        {
            writer.Write(texture.Data);
        }

        foreach (var frame in file.Frames)
        {
            if (frame.IsLink || frame.Texture is not null)
            {
                continue;
            }

            writer.Write(frame.RawData ?? []);
        }
    }

    /// <inheritdoc />
    public override RgbaImage? Decode(ImgFile file, ImgFrame frame)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.IsLink)
        {
            return null;
        }

        if (frame.Texture is { } reference)
        {
            var page = DecodeTexture(file, reference.TextureIndex, frame.Index);
            EnsureRectInside(reference.Rect, page.Width, page.Height, frame.Index, reference.TextureIndex);
            return page.Crop(reference.Rect);
        }

        // 内联 v2 式帧：与 v2 完全同一套解码（NONE/ZLIB + 1555/4444/8888）。
        return base.Decode(file, frame);
    }

    /// <inheritdoc />
    public override byte[]? Encode(ImgFile file, ImgFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.IsLink)
        {
            return null;
        }

        // 纹理帧没有帧级数据区字节；返回 null 而不是"整页字节"，避免调用方误以为这是该帧的像素。
        return frame.Texture is null ? base.Encode(file, frame) : null;
    }

    /// <summary>
    ///     计算帧索引区字节数：LINK 8 字节；纹理帧 36 + 28；内联帧 36。
    ///     <para>
    ///         只覆盖帧索引区，<b>不含</b>色表与纹理表（与旧实现 <c>Album.IndexLength = ms.Length - start</c> 口径一致：
    ///         <c>start</c> 取在纹理表之后）。
    ///     </para>
    /// </summary>
    /// <param name="file">源文件对象。</param>
    /// <returns>帧索引区字节数。</returns>
    internal static long ComputeFrameIndexLength(ImgFile file)
    {
        long length = 0;
        foreach (var frame in file.Frames)
        {
            length += frame.IsLink
                ? LinkIndexSize
                : frame.Texture is null ? FrameIndexSize : FrameIndexSize + TextureUvSize;
        }

        return length;
    }

    /// <summary>
    ///     计算写出后的 IMG 总字节数（用于 v5 头里那个语义不明的 length 字段）。
    /// </summary>
    /// <param name="file">源文件对象。</param>
    /// <returns>总字节数（截断到 <see cref="int.MaxValue" />）。</returns>
    private static int ComputeTotalLength(ImgFile file)
    {
        long total = ImgFile.CommonHeaderSize + 12L + ((long)file.Palette.Count * PaletteIndexCodec.BytesPerColor);
        total += (long)file.Textures.Count * TextureEntrySize;
        total += ComputeFrameIndexLength(file);

        foreach (var texture in file.Textures)
        {
            total += texture.Data.Length;
        }

        foreach (var frame in file.Frames)
        {
            if (!frame.IsLink && frame.Texture is null)
            {
                total += frame.RawData?.Length ?? 0;
            }
        }

        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    /// <summary>校验纹理表项的几何/长度字段（像素格式留到解码时再判，避免读取阶段过度拒绝）。</summary>
    private static void ValidateTexture(ImgTexture texture, int index)
    {
        if (texture.Width <= 0 || texture.Height <= 0
            || texture.Width > MaxDimension || texture.Height > MaxDimension)
        {
            throw new ImgFormatException($"纹理 #{index} 尺寸非法：{texture.Width}x{texture.Height}（上限 {MaxDimension}）。");
        }

        if (texture.Length < 0)
        {
            throw new ImgFormatException($"纹理 #{index} 数据长度非法：{texture.Length}。");
        }

        if (texture.FullLength < 0 || texture.FullLength > MaxRawBytesPerFrame)
        {
            throw new ImgFormatException(
                $"纹理 #{index} 的解压长度非法：{texture.FullLength}（允许 0..{MaxRawBytesPerFrame}）。");
        }

        var pixels = (long)texture.Width * texture.Height;
        if (pixels * 4 > MaxRawBytesPerFrame)
        {
            throw new ImgFormatException(
                $"纹理 #{index} 像素量超过上限：{texture.Width}x{texture.Height}（上限 {MaxRawBytesPerFrame} 字节）。");
        }
    }

    /// <summary>读帧索引区（含纹理帧的 28 字节 UV 扩展）。</summary>
    private static void ReadFrames(Stream stream, ImgFile file)
    {
        for (var i = 0; i < file.DeclaredFrameCount; i++)
        {
            var frame = new ImgFrame
            {
                Index = i,
                Type = (ColorBits)stream.ReadInt32()
            };
            file.AppendFrame(frame);

            if (frame.Type == ColorBits.LINK)
            {
                frame.LinkIndex = stream.ReadInt32();
                continue;
            }

            frame.Compress = (CompressMode)stream.ReadInt32();
            frame.Width = stream.ReadInt32();
            frame.Height = stream.ReadInt32();
            frame.Length = stream.ReadInt32();
            frame.X = stream.ReadInt32();
            frame.Y = stream.ReadInt32();
            frame.FrameWidth = stream.ReadInt32();
            frame.FrameHeight = stream.ReadInt32();

            var pixelBytes = BytesPerPixel(frame.Type);

            if (frame.Length < 0)
            {
                throw new ImgFormatException($"帧 #{i} 数据长度非法：{frame.Length}。");
            }

            // 判别顺序（对旧实现 `type < LINK && length != 0` 的等价展开，但把"length == 0"先挑出来）：
            //   ① type < LINK 且 length == 0  → 纹理帧（绝不做 NONE 长度推导，否则会被误判成内联帧）；
            //   ② type < LINK 且 length != 0  → 内联 v2 帧，NONE 时按几何推导长度；
            //   ③ type >= LINK（DXT 类型）    → 按纹理帧处理（旧实现同样不看 length）。
            if (frame.Type < ColorBits.LINK && frame.Length == 0)
            {
                ReadTextureReference(stream, file, frame, i);
                continue;
            }

            if (frame.Type < ColorBits.LINK)
            {
                if (frame.Compress == CompressMode.NONE)
                {
                    // 与 v2 一致：NONE 时用几何推导长度覆盖索引里的脏值。
                    ValidateGeometry(frame, i, pixelBytes);
                    frame.Length = (int)((long)frame.Width * frame.Height * pixelBytes);
                }

                ValidateGeometry(frame, i, pixelBytes);
                continue;
            }

            ReadTextureReference(stream, file, frame, i);
        }
    }

    /// <summary>读纹理帧的 28 字节 UV 扩展，并把几何统一成子矩形尺寸。</summary>
    private static void ReadTextureReference(Stream stream, ImgFile file, ImgFrame frame, int index)
    {
        var unknown = stream.ReadInt32();
        var textureIndex = stream.ReadInt32();
        var leftUpX = stream.ReadInt32();
        var leftUpY = stream.ReadInt32();
        var rightDownX = stream.ReadInt32();
        var rightDownY = stream.ReadInt32();
        var rotate = stream.ReadInt32();

        if (textureIndex < 0 || textureIndex >= file.Textures.Count)
        {
            throw new ImgFormatException(
                $"帧 #{index} 引用的纹理下标 {textureIndex} 越界（共 {file.Textures.Count} 张纹理）。");
        }

        var width = rightDownX - leftUpX;
        var height = rightDownY - leftUpY;
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
        {
            throw new ImgFormatException(
                $"帧 #{index} 的 UV 子矩形非法：({leftUpX},{leftUpY})-({rightDownX},{rightDownY}) → {width}x{height}。");
        }

        var texture = file.Textures[textureIndex];
        EnsureRectInside(new PixelBounds(leftUpX, leftUpY, width, height), texture.Width, texture.Height, index, textureIndex);

        frame.Texture = new TextureRef
        {
            Unknown = unknown,
            TextureIndex = textureIndex,
            Rect = new PixelBounds(leftUpX, leftUpY, width, height),
            Rotate = rotate
        };

        // 以 UV 子矩形为准：Decode 返回的尺寸必须与 Width/Height 一致，否则上层按帧几何裁画布会错位。
        frame.Width = width;
        frame.Height = height;
    }

    /// <summary>读数据区：先纹理页（按表顺序），再内联帧（按帧顺序）。</summary>
    private static void ReadPayload(Stream stream, ImgFile file)
    {
        foreach (var texture in file.Textures)
        {
            texture.Data = stream.ReadBytesExact(texture.Length);
        }

        foreach (var frame in file.Frames)
        {
            if (frame.IsLink || frame.Texture is not null)
            {
                continue;
            }

            frame.RawData = stream.ReadBytesExact(frame.Length);
        }
    }

    /// <summary>写帧索引区（含纹理帧的 UV 扩展、LINK、内联帧）。</summary>
    private static void WriteFrames(BinaryWriter writer, ImgFile file)
    {
        foreach (var frame in file.Frames)
        {
            writer.Write((int)frame.Type);

            if (frame.IsLink)
            {
                var target = frame.LinkTarget
                              ?? throw new ImgFormatException($"帧 #{frame.Index} 是 LINK 但没有目标帧。");
                writer.Write(target.Index);
                continue;
            }

            writer.Write((int)frame.Compress);
            writer.Write(frame.Width);
            writer.Write(frame.Height);

            if (frame.Texture is { } reference)
            {
                if ((uint)reference.TextureIndex >= (uint)file.Textures.Count)
                {
                    throw new ImgFormatException(
                        $"帧 #{frame.Index} 引用的纹理下标 {reference.TextureIndex} 越界（共 {file.Textures.Count} 张纹理），无法写出。");
                }

                // 纹理帧的 length 恒 0（旧实现读进来就是"保留，固定为0"），写回保持同口径。
                frame.Length = 0;
                writer.Write(0);
                writer.Write(frame.X);
                writer.Write(frame.Y);
                writer.Write(frame.FrameWidth);
                writer.Write(frame.FrameHeight);

                writer.Write(reference.Unknown);
                writer.Write(reference.TextureIndex);
                writer.Write(reference.Rect.X);
                writer.Write(reference.Rect.Y);
                writer.Write(reference.Rect.Right);
                writer.Write(reference.Rect.Bottom);
                writer.Write(reference.Rotate);
                continue;
            }

            frame.Length = frame.RawData?.Length ?? 0;
            writer.Write(frame.Length);
            writer.Write(frame.X);
            writer.Write(frame.Y);
            writer.Write(frame.FrameWidth);
            writer.Write(frame.FrameHeight);
        }
    }

    /// <summary>解码一页纹理（DXT → DDS 解析 + <see cref="DxtDecoder" />；未压缩 → <see cref="ColorCodec" />）。</summary>
    private static RgbaImage DecodeTexture(ImgFile file, int textureIndex, int frameIndex)
    {
        if (textureIndex < 0 || textureIndex >= file.Textures.Count)
        {
            throw new ImgFormatException(
                $"帧 #{frameIndex} 引用的纹理下标 {textureIndex} 越界（共 {file.Textures.Count} 张纹理）。");
        }

        var texture = file.Textures[textureIndex];

        // 长度不符在这里就要给出可读异常，不能带着错误长度去解 DXT/像素。
        if (texture.FullLength <= 0)
        {
            throw new ImgFormatException(
                $"纹理 #{textureIndex}（帧 #{frameIndex} 引用）的解压长度非法：{texture.FullLength}。");
        }

        var raw = ZlibCodec.Decompress(texture.Data, 0, texture.Data.Length, texture.FullLength);

        return texture.IsBlockCompressed
            ? DecodeBlockCompressed(raw, texture, frameIndex)
            : DecodeUncompressed(raw, texture, frameIndex);
    }

    /// <summary>DXT1/3/5：解压结果是完整 DDS 文件，用现有 <see cref="DdsFile" /> + <see cref="DxtDecoder" /> 解码。</summary>
    private static RgbaImage DecodeBlockCompressed(byte[] raw, ImgTexture texture, int frameIndex)
    {
        var dds = DdsFile.Parse(raw);
        if (dds.Width != texture.Width || dds.Height != texture.Height)
        {
            throw new ImgFormatException(
                $"纹理 #{texture.Index}（帧 #{frameIndex} 引用）的 DDS 头尺寸 {dds.Width}x{dds.Height} " +
                $"与纹理表声明 {texture.Width}x{texture.Height} 不一致。");
        }

        var image = new RgbaImage(dds.Width, dds.Height);
        DxtDecoder.Decode(dds.PrimaryMipmap.Data, dds.Width, dds.Height, dds.Format, image);
        return image;
    }

    /// <summary>未压缩纹理：直接按 ColorBits 解 BGRA（旧实现 <c>Texture.CreateFromBitmap</c> 的写出口径）。</summary>
    private static RgbaImage DecodeUncompressed(byte[] raw, ImgTexture texture, int frameIndex)
    {
        if (texture.Type is not (ColorBits.ARGB_1555 or ColorBits.ARGB_4444 or ColorBits.ARGB_8888))
        {
            throw new ImgFormatException(
                $"纹理 #{texture.Index}（帧 #{frameIndex} 引用）的像素格式 0x{(int)texture.Type:X2} " +
                $"（version={texture.Version}）不受支持：既不是 DXT1/3/5，也不是 1555/4444/8888。");
        }

        var bytesPerPixel = ColorCodec.BytesPerPixel(texture.Type);
        var pixels = (long)texture.Width * texture.Height;
        if (pixels * bytesPerPixel > MaxRawBytesPerFrame)
        {
            throw new ImgFormatException(
                $"纹理 #{texture.Index} 像素量超过上限：{texture.Width}x{texture.Height} × {bytesPerPixel}B。");
        }

        var expected = (int)(pixels * bytesPerPixel);
        if (raw.Length < expected)
        {
            throw new ImgFormatException(
                $"纹理 #{texture.Index} 解压后只有 {raw.Length} 字节，" +
                $"但 {texture.Width}x{texture.Height} 的 {texture.Type} 需要 {expected} 字节" +
                $"（声明的 fullLength={texture.FullLength}）。");
        }

        return RgbaImage.FromBgra(ColorCodec.Decode(raw.AsSpan(0, expected), texture.Type, (int)pixels), texture.Width, texture.Height);
    }

    /// <summary>UV 子矩形必须完整落在纹理页内（<c>RgbaImage.Crop</c> 会静默夹取，那会把坏数据画成错位图像）。</summary>
    private static void EnsureRectInside(PixelBounds rect, int pageWidth, int pageHeight, int frameIndex, int textureIndex)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Right > pageWidth || rect.Bottom > pageHeight)
        {
            throw new ImgFormatException(
                $"帧 #{frameIndex} 的 UV 子矩形 {rect} 超出纹理 #{textureIndex} 的 {pageWidth}x{pageHeight}。");
        }
    }
}
