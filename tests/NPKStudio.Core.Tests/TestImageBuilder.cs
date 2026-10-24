using System.Buffers.Binary;
using System.Text;

using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Tests;

/// <summary>
///     逐字节构造 IMG 样本的测试工具。
///     <para>
///         本机没有任何 v1 真实样本（三个客户端抽样 100% 是 v2），所以 v1 只能靠这里手工构造。
///         每个写入位置都注明字节布局，便于与 <c>FORMAT-VERIFIED.md</c> §2/§3 对照。
///     </para>
/// </summary>
internal static class TestImageBuilder
{
    /// <summary>普通帧索引项字节数：type + compress + w + h + length + x + y + fw + fh = 9 × 4。</summary>
    private const int FrameIndexSize = 36;

    /// <summary>LINK 帧索引项字节数：type + targetIndex = 2 × 4。</summary>
    private const int LinkIndexSize = 8;

    /// <summary>v5 纹理表项字节数：version, type, index, length, fullLength, width, height = 7 × 4。</summary>
    private const int TextureEntrySize = 28;

    /// <summary>v5 纹理帧在帧索引项之后的 UV 扩展字节数：7 × 4。</summary>
    private const int TextureUvSize = 28;

    /// <summary>
    ///     构造 v2 IMG。
    ///     <para>布局：<c>"Neople Img File"+0x00(16) + int64 indexLength + int32 version + int32 count + 索引区 + 数据区</c>。</para>
    /// </summary>
    /// <param name="frames">帧定义（顺序即写入顺序）。</param>
    /// <param name="version">版本号，默认 v2。</param>
    /// <returns>完整 IMG 字节。</returns>
    public static byte[] BuildVer2(IReadOnlyList<TestFrame> frames, ImgVersion version = ImgVersion.Ver2)
    {
        var indexLength = 0;
        foreach (var frame in frames)
        {
            indexLength += frame.IsLink ? LinkIndexSize : FrameIndexSize;
        }

        using var stream = new MemoryStream();

        // 0..15：魔数（15 字符 + 0x00）
        stream.Write(Encoding.Latin1.GetBytes("Neople Img File\0"));
        // 16..23：indexLength（int64，只含索引区，不含 32 字节头）
        WriteInt64(stream, indexLength);
        // 24..27：version
        WriteInt32(stream, (int)version);
        // 28..31：count
        WriteInt32(stream, frames.Count);

        // 索引区
        foreach (var frame in frames)
        {
            WriteInt32(stream, (int)frame.Type);
            if (frame.IsLink)
            {
                WriteInt32(stream, frame.LinkTargetIndex);
                continue;
            }

            WriteInt32(stream, (int)frame.Compress);
            WriteInt32(stream, frame.Width);
            WriteInt32(stream, frame.Height);
            WriteInt32(stream, frame.Length);
            WriteInt32(stream, frame.X);
            WriteInt32(stream, frame.Y);
            WriteInt32(stream, frame.FrameWidth);
            WriteInt32(stream, frame.FrameHeight);
        }

        // 数据区：只写非 LINK 帧
        foreach (var frame in frames)
        {
            if (!frame.IsLink)
            {
                stream.Write(frame.Data);
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    ///     构造 v5 IMG（DDS 纹理图集 + 每帧 UV 子矩形）。
    ///     <para>
    ///         布局：<c>"Neople Img File"+0x00(16) + int64 indexLength(只含帧索引区) + int32 version(5) + int32 count
    ///         + int32 textureCount + int32 未知长度字段 + int32 paletteCount + palette
    ///         + 纹理表(textureCount×28) + 帧索引区 + 纹理数据 + 内联帧数据</c>。
    ///     </para>
    ///     <para>
    ///         依据旧实现 <c>FifthHandler.CreateFromStream</c> / <c>AdjustData</c>；本机没有真实 v5 样本，
    ///         所有 v5 测试都用这里手工拼的字节。
    ///     </para>
    /// </summary>
    /// <param name="frames">帧定义；纹理帧需要在 <see cref="TestFrame.Uv" /> 里给出 UV 子矩形。</param>
    /// <param name="textures">纹理表（顺序即下标，帧的 <c>TextureIndex</c> 指向它）。</param>
    /// <param name="palette">色表（v5 也有色表区，未压缩纹理不用它，通常传空）。</param>
    /// <returns>完整 IMG 字节。</returns>
    public static byte[] BuildVer5(
        IReadOnlyList<TestFrame> frames,
        IReadOnlyList<TestTexture> textures,
        IReadOnlyList<Color32>? palette = null)
    {
        var indexLength = 0;
        foreach (var frame in frames)
        {
            indexLength += frame.IsLink
                ? LinkIndexSize
                : frame.Uv is null ? FrameIndexSize : FrameIndexSize + TextureUvSize;
        }

        var colors = palette ?? [];

        using var stream = new MemoryStream();

        // 0..15：魔数；16..23：indexLength（只含帧索引区，不含色表/纹理表）
        stream.Write(Encoding.Latin1.GetBytes("Neople Img File\0"));
        WriteInt64(stream, indexLength);
        WriteInt32(stream, (int)ImgVersion.Ver5);
        WriteInt32(stream, frames.Count);

        // v5 扩展头：纹理数 + 语义不明的长度字段 + 色表
        WriteInt32(stream, textures.Count);
        WriteInt32(stream, 0);
        WriteInt32(stream, colors.Count);
        WritePalette(stream, colors);

        // 纹理表
        var payloads = new byte[textures.Count][];
        for (var i = 0; i < textures.Count; i++)
        {
            payloads[i] = ZlibCodec.Compress(textures[i].Raw);
            var texture = textures[i];
            WriteInt32(stream, texture.Version);
            WriteInt32(stream, (int)texture.Type);
            WriteInt32(stream, texture.Index);
            WriteInt32(stream, texture.LengthOverride ?? payloads[i].Length);
            WriteInt32(stream, texture.FullLengthOverride ?? texture.Raw.Length);
            WriteInt32(stream, texture.Width);
            WriteInt32(stream, texture.Height);
        }

        // 帧索引区
        foreach (var frame in frames)
        {
            WriteInt32(stream, (int)frame.Type);
            if (frame.IsLink)
            {
                WriteInt32(stream, frame.LinkTargetIndex);
                continue;
            }

            WriteInt32(stream, (int)frame.Compress);
            WriteInt32(stream, frame.Width);
            WriteInt32(stream, frame.Height);
            WriteInt32(stream, frame.Length);
            WriteInt32(stream, frame.X);
            WriteInt32(stream, frame.Y);
            WriteInt32(stream, frame.FrameWidth);
            WriteInt32(stream, frame.FrameHeight);

            if (frame.Uv is not { } uv)
            {
                continue;
            }

            WriteInt32(stream, uv.Unknown);
            WriteInt32(stream, uv.TextureIndex);
            WriteInt32(stream, uv.LeftUpX);
            WriteInt32(stream, uv.LeftUpY);
            WriteInt32(stream, uv.RightDownX);
            WriteInt32(stream, uv.RightDownY);
            WriteInt32(stream, uv.Rotate);
        }

        // 数据区：先纹理页，再内联帧
        foreach (var payload in payloads)
        {
            stream.Write(payload);
        }

        foreach (var frame in frames)
        {
            if (!frame.IsLink && frame.Uv is null)
            {
                stream.Write(frame.Data);
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    ///     拼一个最小 DDS 头 + 块数据（v5 的 DXT 纹理解压后就是完整 DDS 文件）。
    ///     <para>只填 <c>DdsFile.Parse</c> 会读的字段：魔数、头长度、宽高、mipmap 数、FourCC；数据区从 128 字节开始。</para>
    /// </summary>
    /// <param name="format">DXT 格式。</param>
    /// <param name="width">宽。</param>
    /// <param name="height">高。</param>
    /// <param name="blocks">块数据（DXT1 每块 8 字节，DXT3/5 每块 16 字节）。</param>
    /// <returns>完整 DDS 字节。</returns>
    public static byte[] BuildDds(DxtFormat format, int width, int height, byte[] blocks)
    {
        var fourCc = format switch
        {
            DxtFormat.Dxt1 => "DXT1",
            DxtFormat.Dxt3 => "DXT3",
            _ => "DXT5"
        };

        var result = new byte[128 + blocks.Length];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(result, 0);
        WriteInt32(result, 4, 124); // dwSize
        WriteInt32(result, 8, 0x00081007); // DDSD_CAPS|HEIGHT|WIDTH|PIXELFORMAT
        WriteInt32(result, 12, height);
        WriteInt32(result, 16, width);
        WriteInt32(result, 28, 1); // dwMipMapCount
        WriteInt32(result, 76, 32); // ddspf.dwSize
        WriteInt32(result, 80, 0x4); // ddspf.dwFlags = DDPF_FOURCC
        Encoding.ASCII.GetBytes(fourCc).CopyTo(result, 84);
        WriteInt32(result, 108, 0x1000); // dwCaps = DDSCAPS_TEXTURE
        blocks.CopyTo(result, 128);
        return result;
    }

    /// <summary>
    ///     构造 v1 IMG（帧数据内联在索引流里）。
    ///     <para>
    ///         布局：<c>"Neople Image File"+0x00(18) + int32 indexLength(恒 0) + int16 保留 + int32 version + int32 count</c>，
    ///         此后每帧"索引 + 该帧数据"紧邻。
    ///     </para>
    /// </summary>
    /// <param name="frames">帧定义。</param>
    /// <returns>完整 IMG 字节。</returns>
    public static byte[] BuildVer1(IReadOnlyList<TestFrame> frames)
    {
        using var stream = new MemoryStream();

        // 0..17：魔数（17 字符 + 0x00）
        stream.Write(Encoding.Latin1.GetBytes("Neople Image File\0"));
        // 18..21：indexLength —— 格式约定：写入恒 0，读侧不依赖
        WriteInt32(stream, 0);
        // 22..23：2 字节保留区
        stream.Write([0x00, 0x00]);
        // 24..27：version
        WriteInt32(stream, (int)ImgVersion.Ver1);
        // 28..31：count
        WriteInt32(stream, frames.Count);

        // 每帧：索引 + 内联数据
        foreach (var frame in frames)
        {
            WriteInt32(stream, (int)frame.Type);
            if (frame.IsLink)
            {
                WriteInt32(stream, frame.LinkTargetIndex);
                continue;
            }

            WriteInt32(stream, (int)frame.Compress);
            WriteInt32(stream, frame.Width);
            WriteInt32(stream, frame.Height);
            WriteInt32(stream, frame.Length);
            WriteInt32(stream, frame.X);
            WriteInt32(stream, frame.Y);
            WriteInt32(stream, frame.FrameWidth);
            WriteInt32(stream, frame.FrameHeight);
            stream.Write(frame.Data);
        }

        return stream.ToArray();
    }

    /// <summary>
    ///     构造 v4 IMG（单张色表 + 1 字节色号）。
    ///     <para>
    ///         布局：<c>"Neople Img File"+0x00(16) + int64 indexLength(不含色表) + int32 version + int32 count
    ///         + int32 paletteCount + paletteCount×4(R,G,B,A) + 索引区 + 数据区</c>。
    ///     </para>
    /// </summary>
    /// <param name="frames">帧定义（色号数据放在 <see cref="TestFrame.Data" />）。</param>
    /// <param name="palette">色表。</param>
    /// <returns>完整 IMG 字节。</returns>
    public static byte[] BuildVer4(IReadOnlyList<TestFrame> frames, IReadOnlyList<Color32> palette)
    {
        using var stream = new MemoryStream();
        WriteV4Header(stream, frames, ImgVersion.Ver4);
        WriteInt32(stream, palette.Count);
        WritePalette(stream, palette);
        WriteFrameIndexAndPayload(stream, frames);
        return stream.ToArray();
    }

    /// <summary>
    ///     构造 v6 IMG（多张色表 + 1 字节色号）。
    ///     <para>
    ///         布局：<c>…公共头… + int32 tableCount + 每表(int32 count + count×4) + 索引区 + 数据区</c>。
    ///     </para>
    /// </summary>
    /// <param name="frames">帧定义（色号数据放在 <see cref="TestFrame.Data" />）。</param>
    /// <param name="tables">色表集合。</param>
    /// <returns>完整 IMG 字节。</returns>
    public static byte[] BuildVer6(IReadOnlyList<TestFrame> frames, IReadOnlyList<IReadOnlyList<Color32>> tables)
    {
        using var stream = new MemoryStream();
        WriteV4Header(stream, frames, ImgVersion.Ver6);
        WriteInt32(stream, tables.Count);
        foreach (var table in tables)
        {
            WriteInt32(stream, table.Count);
            WritePalette(stream, table);
        }

        WriteFrameIndexAndPayload(stream, frames);
        return stream.ToArray();
    }

    /// <summary>v4/v6 公共头：魔数 + indexLength（只含帧索引区）+ version + count。</summary>
    private static void WriteV4Header(Stream stream, IReadOnlyList<TestFrame> frames, ImgVersion version)
    {
        var indexLength = 0;
        foreach (var frame in frames)
        {
            indexLength += frame.IsLink ? LinkIndexSize : FrameIndexSize;
        }

        stream.Write(Encoding.Latin1.GetBytes("Neople Img File\0"));
        WriteInt64(stream, indexLength);
        WriteInt32(stream, (int)version);
        WriteInt32(stream, frames.Count);
    }

    /// <summary>按 R,G,B,A 顺序写色表（与旧实现 Colors.WritePalette 一致）。</summary>
    private static void WritePalette(Stream stream, IReadOnlyList<Color32> palette)
    {
        for (var i = 0; i < palette.Count; i++)
        {
            var color = palette[i];
            stream.WriteByte(color.R);
            stream.WriteByte(color.G);
            stream.WriteByte(color.B);
            stream.WriteByte(color.A);
        }
    }

    /// <summary>写 v2 系帧索引区 + 数据区（v4/v6 与 v2 相同）。</summary>
    private static void WriteFrameIndexAndPayload(Stream stream, IReadOnlyList<TestFrame> frames)
    {
        foreach (var frame in frames)
        {
            WriteInt32(stream, (int)frame.Type);
            if (frame.IsLink)
            {
                WriteInt32(stream, frame.LinkTargetIndex);
                continue;
            }

            WriteInt32(stream, (int)frame.Compress);
            WriteInt32(stream, frame.Width);
            WriteInt32(stream, frame.Height);
            WriteInt32(stream, frame.Length);
            WriteInt32(stream, frame.X);
            WriteInt32(stream, frame.Y);
            WriteInt32(stream, frame.FrameWidth);
            WriteInt32(stream, frame.FrameHeight);
        }

        foreach (var frame in frames)
        {
            if (!frame.IsLink)
            {
                stream.Write(frame.Data);
            }
        }
    }

    /// <summary>
    ///     构造一个未压缩（<c>CompressMode.NONE</c>）帧的环形渐变 ARGB1555 像素数据。
    ///     <para>像素值刻意让每帧不同，便于断言"帧与帧的数据没有串位"。</para>
    /// </summary>
    /// <param name="width">宽。</param>
    /// <param name="height">高。</param>
    /// <param name="seed">帧序号，用于制造差异。</param>
    /// <returns>长度为 <c>width * height * 2</c> 的字节。</returns>
    public static byte[] BuildArgb1555Pixels(int width, int height, byte seed)
    {
        var data = new byte[width * height * 2];
        for (var i = 0; i < width * height; i++)
        {
            // 1555：a:1 r:5 g:5 b:5（小端 16 位）
            var r = (i + seed) % 32;
            var g = (i * 2 + seed) % 32;
            var b = (i * 3 + seed) % 32;
            var value = (ushort)(0x8000 | (r << 10) | (g << 5) | b);
            data[i * 2] = (byte)(value & 0xFF);
            data[(i * 2) + 1] = (byte)(value >> 8);
        }

        return data;
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), value);
    }

    /// <summary>
    ///     v5 纹理页定义。
    /// </summary>
    internal sealed class TestTexture
    {
        /// <summary>纹理版本：1=DXT1 / 3=DXT3 / 5=DXT5（非 DXT 时无意义）。</summary>
        public int Version { get; init; } = 1;

        /// <summary>像素格式。</summary>
        public ColorBits Type { get; init; } = ColorBits.ARGB_1555;

        /// <summary>纹理表里的 index 字段。</summary>
        public int Index { get; init; }

        /// <summary>宽。</summary>
        public int Width { get; init; }

        /// <summary>高。</summary>
        public int Height { get; init; }

        /// <summary>未压缩数据（未压缩像素，或"128 字节 DDS 头 + DXT 块"）。</summary>
        public byte[] Raw { get; init; } = [];

        /// <summary>覆盖写进纹理表的 length（用于构造"声明长度与实际不符"的坏样本）。</summary>
        public int? LengthOverride { get; init; }

        /// <summary>覆盖写进纹理表的 fullLength（用于构造"解压长度与实际不符"的坏样本）。</summary>
        public int? FullLengthOverride { get; init; }
    }

    /// <summary>
    ///     v5 纹理帧的 UV 扩展（7 × int32）。
    /// </summary>
    internal sealed class TestUv
    {
        /// <summary>未知字段，原样读写。</summary>
        public int Unknown { get; init; }

        /// <summary>源纹理下标。</summary>
        public int TextureIndex { get; init; }

        /// <summary>子矩形左上角 X。</summary>
        public int LeftUpX { get; init; }

        /// <summary>子矩形左上角 Y。</summary>
        public int LeftUpY { get; init; }

        /// <summary>子矩形右下角 X。</summary>
        public int RightDownX { get; init; }

        /// <summary>子矩形右下角 Y。</summary>
        public int RightDownY { get; init; }

        /// <summary>旧实现的 top 字段（旋转标志，原样读写保留）。</summary>
        public int Rotate { get; init; }
    }

    /// <summary>
    ///     帧定义（测试用不可变描述）。
    /// </summary>
    internal sealed class TestFrame
    {
        /// <summary>像素格式。</summary>
        public ColorBits Type { get; init; } = ColorBits.ARGB_1555;

        /// <summary>压缩方式。</summary>
        public CompressMode Compress { get; init; } = CompressMode.NONE;

        /// <summary>宽。</summary>
        public int Width { get; init; }

        /// <summary>高。</summary>
        public int Height { get; init; }

        /// <summary>索引里声明的数据长度（LINK 帧忽略）。</summary>
        public int Length { get; init; }

        /// <summary>帧左上角 X。</summary>
        public int X { get; init; }

        /// <summary>帧左上角 Y。</summary>
        public int Y { get; init; }

        /// <summary>帧域宽。</summary>
        public int FrameWidth { get; init; }

        /// <summary>帧域高。</summary>
        public int FrameHeight { get; init; }

        /// <summary>原始数据字节。</summary>
        public byte[] Data { get; init; } = [];

        /// <summary>LINK 帧的目标下标。</summary>
        public int LinkTargetIndex { get; init; } = -1;

        /// <summary>v5 纹理帧的 UV 扩展；<c>null</c> 表示"内联 v2 式帧"（不走纹理）。</summary>
        public TestUv? Uv { get; init; }

        /// <summary>是否链接帧。</summary>
        public bool IsLink => Type == ColorBits.LINK;
    }
}
