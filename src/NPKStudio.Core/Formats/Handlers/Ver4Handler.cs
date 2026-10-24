using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats.Handlers;

/// <summary>
///     v4（<c>"Neople Img File"</c> + 单张色表）处理器。
///     <para>
///         布局：公共头 32 字节 → <b>色表</b>（<c>int32 count</c> + <c>count × 4</c> 字节，每色 R,G,B,A）
///         → 与 v2 相同的帧索引区 → 数据区。索引区长度不含色表区。
///     </para>
///     <para>
///         像素是 <b>1 字节色号</b>，不是 1555/4444/8888；索引区里的 type 仍沿用 0x0E（旧实现
///         <c>SecondHandler.ConvertToVersion</c> 在切到 v4 时把普通帧统一改成 <c>ARGB_1555</c>）。
///         因此 <see cref="ImgFrame.Type" /> 只用于保持文件字段原值，解码时一律按色号处理。
///     </para>
///     <para>LINK 帧、NONE/ZLIB、负坐标的处理与 v2 一致（复用基类的索引/数据区读写）。</para>
/// </summary>
internal sealed class Ver4Handler : ImgHandlerBase
{
    /// <inheritdoc />
    public override ImgVersion Version => ImgVersion.Ver4;

    /// <inheritdoc />
    public override void ReadIndex(Stream stream, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(file);

        var count = stream.ReadInt32();
        file.SetPalette(PaletteIndexCodec.ReadColors(stream, count));

        // bytesPerPixel: 1 —— NONE 帧的数据长度是 Width × Height，不是 ×2。
        ReadFrameIndex(stream, file, file.DeclaredFrameCount, bytesPerPixel: 1);
        ReadPayloadSequential(stream, file);
        ResolveLinks(file);
    }

    /// <inheritdoc />
    public override void WriteIndex(BinaryWriter writer, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(file);

        // IndexLength 只含帧索引区（不含色表），与读侧/旧实现口径一致。
        file.IndexLength = ComputeIndexLength(file);
        WriteCommonHeader(writer, file);

        writer.Write(file.Palette.Count);
        PaletteIndexCodec.WriteColors(writer, file.Palette);

        WriteFrameIndex(writer, file);
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

        var indices = PaletteIndexCodec.ReadIndices(frame)
            ?? throw new ImgFormatException($"帧 #{frame.Index} 无数据可解码。");
        var count = ComputePixelCount(frame, 1);
        return RgbaImage.FromBgra(
            PaletteIndexCodec.Decode(indices, file.Palette, count, frame.Index),
            frame.Width,
            frame.Height);
    }

    /// <inheritdoc />
    public override byte[]? ReadUncompressedPixels(ImgFile file, ImgFrame frame)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(frame);

        return PaletteIndexCodec.ReadIndices(frame);
    }
}
