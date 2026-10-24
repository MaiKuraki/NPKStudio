namespace NPKStudio.Core.Imaging.Gif;

/// <summary>
///     动画 GIF（GIF89a）编码器：零第三方依赖，LZW 自己实现。
///     <para>
///         结构：文件头 + 逻辑屏描述符 → <c>NETSCAPE2.0</c> 循环扩展 → 每帧
///         （图形控制扩展 + 图像描述符 + 本地色表 + LZW 数据）→ 结尾 <c>0x3B</c>。
///         每帧写<b>本地色表</b>（不写全局色表），因此各帧可以使用不同调色板。
///     </para>
///     <para>
///         颜色规则：alpha &lt; 128 的像素映射到透明色索引；单帧不透明色 ≤255（有透明）或 ≤256（无透明）时
///         精确映射、不降质；超过才用中位切分量化。色表长度向上取 2 的幂（GIF 规范要求）。
///     </para>
///     <para>
///         <b>不做的事</b>：不写全局色表、不写交错、不做帧间差分（每帧都是完整画面）。
///         这些都不影响正确性，只是文件略大。
///     </para>
/// </summary>
public static class GifEncoder
{
    /// <summary>帧延时的下限（毫秒）。</summary>
    public const int MinDelayMilliseconds = 20;

    /// <summary>帧延时的上限（毫秒）。</summary>
    public const int MaxDelayMilliseconds = 1000;

    /// <summary>
    ///     把帧序列编码成 GIF 字节数组。
    /// </summary>
    /// <param name="frames">帧序列，不能为空；尺寸可以不同（各自写在自己的 x=0,y=0 处）。</param>
    /// <param name="delayMilliseconds">每帧延时（毫秒），会被夹到 20..1000 并向下取整到 10ms。</param>
    /// <param name="loopCount">循环次数，0 表示无限循环。</param>
    /// <returns>完整的 GIF89a 字节。</returns>
    /// <exception cref="ArgumentNullException">参数为 <c>null</c>。</exception>
    /// <exception cref="ArgumentException">帧序列为空，或某帧尺寸超过 GIF 的 16 位上限。</exception>
    public static byte[] Encode(IReadOnlyList<RgbaImage> frames, int delayMilliseconds = 100, int loopCount = 0)
    {
        using var stream = new MemoryStream();
        Write(stream, frames, delayMilliseconds, loopCount);
        return stream.ToArray();
    }

    /// <summary>
    ///     把帧序列写入 GIF 流。
    /// </summary>
    /// <param name="output">目标流（从当前位置开始写）。</param>
    /// <param name="frames">帧序列，不能为空。</param>
    /// <param name="delayMilliseconds">每帧延时（毫秒），夹到 20..1000。</param>
    /// <param name="loopCount">循环次数，0 表示无限循环。</param>
    /// <exception cref="ArgumentNullException">参数为 <c>null</c>。</exception>
    /// <exception cref="ArgumentException">帧序列为空，或某帧尺寸超过 65535。</exception>
    public static void Write(Stream output, IReadOnlyList<RgbaImage> frames, int delayMilliseconds = 100, int loopCount = 0)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("GIF 至少需要一帧。", nameof(frames));
        }

        var screenWidth = 0;
        var screenHeight = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (frame.Width > ushort.MaxValue || frame.Height > ushort.MaxValue)
            {
                throw new ArgumentException($"帧 #{i} 尺寸 {frame.Width}x{frame.Height} 超出 GIF 上限 65535。", nameof(frames));
            }

            if (frame.Width > screenWidth)
            {
                screenWidth = frame.Width;
            }

            if (frame.Height > screenHeight)
            {
                screenHeight = frame.Height;
            }
        }

        // GIF 的延时单位是 1/100 秒；20..1000ms → 2..100。
        var delay = Math.Clamp(delayMilliseconds, MinDelayMilliseconds, MaxDelayMilliseconds) / 10;
        var loop = Math.Clamp(loopCount, 0, ushort.MaxValue);

        WriteHeader(output, screenWidth, screenHeight);
        WriteLoopExtension(output, loop);

        for (var i = 0; i < frames.Count; i++)
        {
            WriteFrame(output, frames[i], delay);
        }

        output.WriteByte(0x3B);
    }

    private static void WriteHeader(Stream output, int width, int height)
    {
        output.Write("GIF89a"u8);

        WriteUInt16(output, (ushort)width);
        WriteUInt16(output, (ushort)height);

        // 无全局色表（bit7=0）、色彩分辨率 8 位（bits 4-6 = 111）、无排序、GCT 大小 0。
        output.WriteByte(0x70);

        // 背景色索引与像素宽高比（无全局色表时均无实际意义）。
        output.WriteByte(0x00);
        output.WriteByte(0x00);
    }

    private static void WriteLoopExtension(Stream output, int loopCount)
    {
        output.WriteByte(0x21); // 扩展引导
        output.WriteByte(0xFF); // 应用扩展
        output.WriteByte(0x0B); // 块长 11

        output.Write("NETSCAPE2.0"u8);

        output.WriteByte(0x03); // 子块长
        output.WriteByte(0x01); // 子块 ID：循环
        WriteUInt16(output, (ushort)loopCount);
        output.WriteByte(0x00); // 块终止
    }

    private static void WriteFrame(Stream output, RgbaImage frame, int delay)
    {
        var palette = GifFramePalette.Build(frame);
        var transparent = palette.TransparentIndex >= 0;

        // 图形控制扩展：处置方法 2（恢复背景）、延时、透明色索引。
        output.WriteByte(0x21);
        output.WriteByte(0xF9);
        output.WriteByte(0x04);
        output.WriteByte((byte)((2 << 2) | (transparent ? 1 : 0)));
        WriteUInt16(output, (ushort)delay);
        output.WriteByte((byte)(transparent ? palette.TransparentIndex : 0));
        output.WriteByte(0x00);

        // 图像描述符：位置 (0,0) + 本帧尺寸 + 有本地色表。
        output.WriteByte(0x2C);
        WriteUInt16(output, 0);
        WriteUInt16(output, 0);
        WriteUInt16(output, (ushort)frame.Width);
        WriteUInt16(output, (ushort)frame.Height);

        // 本地色表标志 bit7=1，表大小字段 = log2(tableSize) - 1。
        output.WriteByte((byte)(0x80 | (palette.Bits - 1)));

        var rgb = palette.Rgb;
        output.Write(rgb, 0, rgb.Length);

        output.WriteByte((byte)palette.MinCodeSize);
        new GifLzwEncoder(output, palette.MinCodeSize).Encode(palette.Indices);
    }

    private static void WriteUInt16(Stream output, ushort value)
    {
        output.WriteByte((byte)(value & 0xFF));
        output.WriteByte((byte)(value >> 8));
    }
}
