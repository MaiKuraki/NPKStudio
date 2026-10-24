using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats;

/// <summary>
///     v4/v6 调色板索引像素的编解码：色表字节读写、色号 → BGRA、BGRA → 就近色号。
///     <para>
///         <b>色表字节序是 R,G,B,A（不是 B,G,R,A）。</b>
///         依据是旧实现 <c>ExtractorSharp.Core/Lib/Colors.cs</c>：<c>ReadPalette</c> 用
///         <c>Color.FromArgb(data[3], data[0], data[1], data[2])</c>（即 byte0=R、byte1=G、byte2=B、byte3=A），
///         <c>WritePalette</c> 对称写 <c>{R,G,B,A}</c>。
///         <b>当前没有 v4/v6 真实样本可用于核对</b>（仓库自带的合成样本全是 v2），
///         因此该顺序属"按旧实现推定"。
///     </para>
///     <para>
///         文件里的色号是 1 字节，因此色表最多 256 项；超出即视为损坏并抛 <see cref="ImgFormatException" />，
///         而不是旧实现的 <c>% count</c> 静默取模（那会把损坏数据渲染成错误颜色）。
///     </para>
/// </summary>
internal static class PaletteIndexCodec
{
    /// <summary>色表每一色的字节数（R,G,B,A）。</summary>
    internal const int BytesPerColor = 4;

    /// <summary>色表项数上限：色号是 1 字节，最多 256 项。</summary>
    internal const int MaxEntries = 256;

    /// <summary>
    ///     读一张色表（调用方已读过 count）。
    /// </summary>
    /// <param name="stream">源流，定位到色表起点。</param>
    /// <param name="count">颜色数量。</param>
    /// <param name="maxEntries">
    ///     允许的最大项数。默认 <see cref="MaxEntries" />（v4/v6 的色号是 1 字节，硬上限 256）；
    ///     v5 的色表只做展示、不参与纹理解码，因此放宽，避免因为一张装饰性色表就拒绝整个文件。
    /// </param>
    /// <returns>颜色数组，字节序按 R,G,B,A 解析。</returns>
    /// <exception cref="ImgFormatException">数量越界。</exception>
    internal static Color32[] ReadColors(Stream stream, int count, int maxEntries = MaxEntries)
    {
        if (count < 0 || count > maxEntries)
        {
            throw new ImgFormatException($"色表项数非法：{count}（允许 0..{maxEntries}）。");
        }

        var bytes = stream.ReadBytesExact(count * BytesPerColor);
        var colors = new Color32[count];
        for (var i = 0; i < count; i++)
        {
            var offset = i * BytesPerColor;
            colors[i] = new Color32(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]);
        }

        return colors;
    }

    /// <summary>
    ///     写一张色表（不写 count）。
    /// </summary>
    /// <param name="writer">目标写入器。</param>
    /// <param name="colors">颜色序列。</param>
    internal static void WriteColors(BinaryWriter writer, IReadOnlyList<Color32> colors)
    {
        for (var i = 0; i < colors.Count; i++)
        {
            var color = colors[i];
            writer.Write(color.R);
            writer.Write(color.G);
            writer.Write(color.B);
            writer.Write(color.A);
        }
    }

    /// <summary>
    ///     取出帧的<b>未压缩</b>色号字节（每像素 1 字节）。
    /// </summary>
    /// <param name="frame">目标帧。</param>
    /// <returns>长度恰为 <c>Width × Height</c> 的色号数组；LINK 帧返回 <c>null</c>。</returns>
    /// <exception cref="ImgFormatException">尺寸非法、长度不足或压缩方式不支持。</exception>
    internal static byte[]? ReadIndices(ImgFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.IsLink)
        {
            return null;
        }

        if (frame.Width <= 0 || frame.Height <= 0
            || frame.Width > Handlers.ImgHandlerBase.MaxDimension
            || frame.Height > Handlers.ImgHandlerBase.MaxDimension)
        {
            throw new ImgFormatException($"帧 #{frame.Index} 尺寸非法：{frame.Width}x{frame.Height}。");
        }

        var expected = (int)((long)frame.Width * frame.Height);
        if (expected > Handlers.ImgHandlerBase.MaxRawBytesPerFrame)
        {
            throw new ImgFormatException($"帧 #{frame.Index} 像素量超过上限：{frame.Width}x{frame.Height}。");
        }

        var raw = frame.RawData ?? [];
        return frame.Compress switch
        {
            CompressMode.NONE => raw.Length >= expected
                ? raw[..expected]
                : throw new ImgFormatException(
                    $"帧 #{frame.Index} 未压缩色号长度不足：期望 {expected} 字节，实际 {raw.Length} 字节。"),

            CompressMode.ZLIB => ZlibCodec.Decompress(raw, 0, raw.Length, expected),

            _ => throw new ImgFormatException(
                $"帧 #{frame.Index} 的压缩方式 {frame.Compress}（0x{(int)frame.Compress:X2}）在 v4/v6 中不受支持。")
        };
    }

    /// <summary>
    ///     把色号展开为 BGRA8888。
    /// </summary>
    /// <param name="indices">每像素 1 字节的色号。</param>
    /// <param name="palette">色表。</param>
    /// <param name="pixelCount">像素数量。</param>
    /// <param name="frameIndex">帧下标（仅用于异常文案）。</param>
    /// <returns>长度为 <c>pixelCount × 4</c> 的 BGRA 缓冲。</returns>
    /// <exception cref="ImgFormatException">色号越界或色表为空。</exception>
    internal static byte[] Decode(ReadOnlySpan<byte> indices, IReadOnlyList<Color32> palette, int pixelCount, int frameIndex)
    {
        if (indices.Length < pixelCount)
        {
            throw new ImgFormatException(
                $"帧 #{frameIndex} 的色号数据不足：需要 {pixelCount} 字节，实际 {indices.Length} 字节。");
        }

        if (palette.Count == 0)
        {
            throw new ImgFormatException($"帧 #{frameIndex} 所在 IMG 没有色表，无法展开色号。");
        }

        var result = new byte[checked(pixelCount * 4)];
        for (var i = 0; i < pixelCount; i++)
        {
            var index = indices[i];
            if (index >= palette.Count)
            {
                throw new ImgFormatException(
                    $"帧 #{frameIndex} 的色号 {index} 超出色表长度 {palette.Count}（数据损坏或版本判定错误）。");
            }

            var color = palette[index];
            var offset = i * 4;
            result[offset] = color.B;
            result[offset + 1] = color.G;
            result[offset + 2] = color.R;
            result[offset + 3] = color.A;
        }

        return result;
    }

    /// <summary>
    ///     把 BGRA8888 反向量化为色号：色表里有的颜色精确命中，没有的取欧氏距离最近的一项。
    ///     <para>色表最多 256 项，直接线性扫描；单次编码是用户操作路径，不是热路径。</para>
    ///     <para>
    ///         距离包含 alpha，因此全透明像素会优先命中色表里的透明项（A=0）；
    ///         若色表里没有透明项，透明像素只能退化为最近的实色——这与 v4/v6 的调色板模型一致。
    ///     </para>
    /// </summary>
    /// <param name="bgra">BGRA8888 缓冲，长度必须是 4 的整数倍。</param>
    /// <param name="palette">色表。</param>
    /// <param name="frameIndex">帧下标（仅用于异常文案）。</param>
    /// <returns>每像素 1 字节的色号。</returns>
    /// <exception cref="ImgFormatException">色表为空。</exception>
    internal static byte[] Encode(ReadOnlySpan<byte> bgra, IReadOnlyList<Color32> palette, int frameIndex)
    {
        if (bgra.Length % 4 != 0)
        {
            throw new ArgumentException($"BGRA 缓冲长度 {bgra.Length} 不是 4 的整数倍。", nameof(bgra));
        }

        if (palette.Count == 0)
        {
            throw new ImgFormatException($"帧 #{frameIndex} 所在 IMG 没有色表，无法把像素量化为色号。");
        }

        var pixelCount = bgra.Length / 4;
        var result = new byte[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * 4;
            result[i] = FindNearest(bgra[offset], bgra[offset + 1], bgra[offset + 2], bgra[offset + 3], palette);
        }

        return result;
    }

    /// <summary>
    ///     在色表中找与指定 BGRA 最接近的项（含 alpha 的平方欧氏距离），精确命中立即返回。
    /// </summary>
    private static byte FindNearest(byte b, byte g, byte r, byte a, IReadOnlyList<Color32> palette)
    {
        var bestIndex = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < palette.Count; i++)
        {
            var color = palette[i];
            var dr = r - color.R;
            var dg = g - color.G;
            var db = b - color.B;
            var da = a - color.A;
            var distance = (dr * dr) + (dg * dg) + (db * db) + (da * da);
            if (distance == 0)
            {
                return (byte)i;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return (byte)bestIndex;
    }
}
