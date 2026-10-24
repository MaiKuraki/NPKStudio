using System.Buffers.Binary;
using System.Text;

using NPKStudio.Core.Exceptions;

namespace NPKStudio.Core.Imaging;

/// <summary>
///     一个 DDS 纹理的单个 mipmap 层。仅作为纯数据容器，不解码。
/// </summary>
public sealed class DdsMipmap
{
    /// <summary>
    ///     该层的宽度（像素）。
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    ///     该层的高度（像素）。
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    ///     该层的<b>压缩块原始字节</b>（未解码），长度 = <c>ceil(Width/4) * ceil(Height/4) * 块字节数</c>。
    /// </summary>
    public byte[] Data { get; init; } = [];

    /// <summary>
    ///     返回可读文本，便于日志定位。
    /// </summary>
    public override string ToString()
    {
        return $"DdsMipmap({Width}x{Height}, {Data.Length} bytes)";
    }
}

/// <summary>
///     DDS 文件解析结果（只解析头部与各 mipmap 的原始块数据，不做 DXT 解码）。
///     <para>
///         头部布局（小端）：<c>0: magic "DDS "</c>、<c>4: dwSize(=124)</c>、<c>12: dwHeight</c>、
///         <c>16: dwWidth</c>、<c>28: dwMipMapCount</c>、<c>84: ddspf.dwFourCC</c>，
///         数据区起点 = <c>4 + dwSize</c>（正常为 128）。
///     </para>
/// </summary>
public sealed class DdsFile
{
    /// <summary>DDS 魔数 <c>"DDS "</c>（小端读作 0x20534444）。</summary>
    public const uint Magic = 0x20534444;

    private const uint FourCcDxt1 = 0x31545844;
    private const uint FourCcDxt3 = 0x33545844;
    private const uint FourCcDxt5 = 0x35545844;

    private DdsFile(int width, int height, DxtFormat format, DdsMipmap[] mipmaps)
    {
        Width = width;
        Height = height;
        Format = format;
        Mipmaps = mipmaps;
    }

    /// <summary>
    ///     纹理宽度（来自头部，第一层 mipmap 的尺寸）。
    /// </summary>
    public int Width { get; }

    /// <summary>
    ///     纹理高度（来自头部，第一层 mipmap 的尺寸）。
    /// </summary>
    public int Height { get; }

    /// <summary>
    ///     块压缩格式（由 <c>dwFourCC</c> 判定）。架构文档未列出该属性，但解码必需，故补充。
    /// </summary>
    public DxtFormat Format { get; }

    /// <summary>
    ///     各 mipmap 层，下标 0 即主层（v5 只用主层）。
    /// </summary>
    public DdsMipmap[] Mipmaps { get; }

    /// <summary>
    ///     主 mipmap 层（下标 0）。
    /// </summary>
    public DdsMipmap PrimaryMipmap => Mipmaps[0];

    /// <summary>
    ///     解析 DDS 字节流。
    /// </summary>
    /// <param name="data">完整的 DDS 数据。</param>
    /// <returns>解析结果。</returns>
    /// <exception cref="ImgFormatException">
    ///     长度不足 128 字节、魔数错误、<c>dwFourCC</c> 不是 DXT1/DXT3/DXT5，或主层数据长度不足。
    /// </exception>
    public static DdsFile Parse(ReadOnlySpan<byte> data)
    {
        const int minHeaderSize = 128;
        if (data.Length < minHeaderSize)
        {
            throw new ImgFormatException($"DDS 数据长度 {data.Length} 小于最小头部 {minHeaderSize} 字节。");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
        {
            throw new ImgFormatException("DDS 魔数错误：文件头不是 \"DDS \"。");
        }

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(12));
        var width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(16));
        var mipCountRaw = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(28));
        var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(84));

        if (width <= 0 || height <= 0)
        {
            throw new ImgFormatException($"DDS 头部尺寸非法：{width}x{height}。");
        }

        var format = fourCc switch
        {
            FourCcDxt1 => DxtFormat.Dxt1,
            FourCcDxt3 => DxtFormat.Dxt3,
            FourCcDxt5 => DxtFormat.Dxt5,
            _ => throw new ImgFormatException(
                $"不支持的 DDS 像素格式 FourCC：\"{Encoding.ASCII.GetString(data.Slice(84, 4))}\"（仅支持 DXT1/DXT3/DXT5）。")
        };

        var dataOffset = 4 + (headerSize > 0 ? headerSize : 124);
        if (dataOffset < minHeaderSize)
        {
            dataOffset = minHeaderSize;
        }

        var blockBytes = format == DxtFormat.Dxt1 ? 8 : 16;
        var mipCount = Math.Max(1, mipCountRaw);
        var mipmaps = new List<DdsMipmap>(mipCount);
        var cursor = dataOffset;
        var levelWidth = width;
        var levelHeight = height;

        for (var level = 0; level < mipCount; level++)
        {
            var blocksX = (levelWidth + 3) / 4;
            var blocksY = (levelHeight + 3) / 4;
            var length = checked(blocksX * blocksY * blockBytes);
            if (cursor + length > data.Length)
            {
                if (level == 0)
                {
                    throw new ImgFormatException(
                        $"DDS 主层数据长度不足：{levelWidth}x{levelHeight} 需要 {length} 字节，实际可用 {data.Length - cursor} 字节。");
                }

                // 后续 mipmap 缺失时容忍（DNF 的纹理基本都是单层）。
                break;
            }

            mipmaps.Add(new DdsMipmap
            {
                Width = levelWidth,
                Height = levelHeight,
                Data = data.Slice(cursor, length).ToArray()
            });

            cursor += length;
            levelWidth = Math.Max(1, levelWidth >> 1);
            levelHeight = Math.Max(1, levelHeight >> 1);

            if (levelWidth == 1 && levelHeight == 1)
            {
                break;
            }
        }

        return new DdsFile(width, height, format, mipmaps.ToArray());
    }

    /// <summary>
    ///     返回可读文本，便于日志定位。
    /// </summary>
    public override string ToString()
    {
        return $"DdsFile({Width}x{Height}, {Format}, {Mipmaps.Length} mipmap(s))";
    }
}
