using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats.Handlers;

/// <summary>
///     v6（<c>"Neople Img File"</c> + 多张色表）处理器。
///     <para>
///         色表区：<c>int32 tableCount</c>，随后每表 <c>int32 count + count × 4</c> 字节（每色 R,G,B,A）。
///         其后与 v2 相同的帧索引区 + 数据区；<c>IndexLength</c> 只算帧索引区，不含色表区。
///     </para>
///     <para>
///         <b>帧用哪张表</b>：索引区里没有"帧 → 表"的字段（每帧仍是 36/8 字节），
///         旧实现以文件级 <c>Album.CurrentTable</c>（默认 0）为准。这里同样固定用第 0 张表解码/量化，
///         其余表通过 <see cref="ImgFile.Palettes" /> 暴露给上层选择。
///     </para>
///     <para>像素是 1 字节色号；LINK、NONE/ZLIB、负坐标与 v2 一致。</para>
/// </summary>
internal sealed class Ver6Handler : ImgHandlerBase
{
    /// <summary>色表数量的护栏。<b>不是格式限制</b>：只用于拦截损坏文件声明的巨量色表。</summary>
    private const int MaxTables = 256;

    /// <inheritdoc />
    public override ImgVersion Version => ImgVersion.Ver6;

    /// <inheritdoc />
    public override void ReadIndex(Stream stream, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(file);

        var tableCount = stream.ReadInt32();
        if (tableCount < 0 || tableCount > MaxTables)
        {
            throw new ImgFormatException($"v6 色表数量非法：{tableCount}（允许 0..{MaxTables}）。");
        }

        for (var i = 0; i < tableCount; i++)
        {
            var count = stream.ReadInt32();
            file.AddPalette(PaletteIndexCodec.ReadColors(stream, count));
        }

        // 主调色板出口（ImgFile.Palette）与第 0 张表保持一致，避免两处口径不同。
        if (file.Palettes.Count > 0)
        {
            file.SetPalette(file.Palettes[0]);
        }

        ReadFrameIndex(stream, file, file.DeclaredFrameCount, bytesPerPixel: 1);
        ReadPayloadSequential(stream, file);
        ResolveLinks(file);
    }

    /// <inheritdoc />
    public override void WriteIndex(BinaryWriter writer, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(file);

        file.IndexLength = ComputeIndexLength(file);
        WriteCommonHeader(writer, file);

        writer.Write(file.Palettes.Count);
        for (var i = 0; i < file.Palettes.Count; i++)
        {
            var table = file.Palettes[i];
            writer.Write(table.Count);
            PaletteIndexCodec.WriteColors(writer, table);
        }

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
            PaletteIndexCodec.Decode(indices, ResolvePalette(file), count, frame.Index),
            frame.Width,
            frame.Height);
    }

    /// <inheritdoc />
    public override byte[]? Encode(ImgFile file, ImgFrame frame)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(frame);

        return PaletteIndexCodec.ReadIndices(frame);
    }

    /// <summary>解码/量化使用的色表（第 0 张；没有表时回落到 <see cref="ImgFile.Palette" />）。</summary>
    private static IReadOnlyList<Color32> ResolvePalette(ImgFile file)
    {
        return file.Palettes.Count > 0 ? file.Palettes[0] : file.Palette;
    }
}
