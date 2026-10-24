namespace NPKStudio.Core.Formats;

/// <summary>
///     IMG 中的一帧。
///     <para>
///         <b>本类型不持有任何位图</b>（D-10 根因）：只保存文件里的原始字节 <see cref="RawData" /> 与几何信息。
///         解码结果由桌面层的 <c>FrameImageCache</c> 按帧引用缓存；编辑只会整段换掉 <see cref="RawData" />，
///         缓存据此自动失效，无需手工通知。
///     </para>
/// </summary>
public sealed class ImgFrame
{
    /// <summary>
    ///     帧在 IMG 内的下标（0 基）。
    /// </summary>
    public int Index { get; internal set; }

    /// <summary>
    ///     像素格式：0x0E 1555 / 0x0F 4444 / 0x10 8888 / 0x11 LINK / 0x12~0x14 DXT。
    /// </summary>
    public ColorBits Type { get; set; } = ColorBits.ARGB_1555;

    /// <summary>
    ///     压缩方式：0x05 NONE / 0x06 ZLIB / 0x07 DDS_ZLIB。
    /// </summary>
    public CompressMode Compress { get; set; } = CompressMode.ZLIB;

    /// <summary>
    ///     帧左上角在角色坐标系中的横坐标，<b>允许为负</b>。
    /// </summary>
    public int X { get; set; }

    /// <summary>
    ///     帧左上角在角色坐标系中的纵坐标，<b>允许为负</b>。
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    ///     帧图像宽度（像素）。
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    ///     帧图像高度（像素）。
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    ///     帧数据在文件中的字节数（压缩后）。<c>Compress == NONE</c> 时由几何推导，读取时会覆盖索引里的值。
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    ///     帧域宽度（角色坐标系的画布尺寸）。
    /// </summary>
    public int FrameWidth { get; set; }

    /// <summary>
    ///     帧域高度（角色坐标系的画布尺寸）。
    /// </summary>
    public int FrameHeight { get; set; }

    /// <summary>
    ///     文件中的原始字节（可能已 zlib 压缩）。<b>写盘时原样输出</b>，故读写往返逐字节一致。
    /// </summary>
    public byte[] RawData { get; set; } = [];

    /// <summary>
    ///     LINK 帧指向的目标帧；无效链接或非 LINK 帧为 <c>null</c>。
    /// </summary>
    public ImgFrame? LinkTarget { get; set; }

    /// <summary>
    ///     v5 的纹理引用信息（仅 v5 帧非空）。
    /// </summary>
    public TextureRef? Texture { get; set; }

    /// <summary>
    ///     是否为链接帧（不占数据区，解码结果为 <c>null</c>）。
    /// </summary>
    public bool IsLink => Type == ColorBits.LINK;

    /// <summary>
    ///     LINK 帧在索引区中记录的目标下标，解析完成后清为 -1。
    /// </summary>
    internal int LinkIndex { get; set; } = -1;

    /// <summary>
    ///     返回可读文本，便于日志定位。
    /// </summary>
    public override string ToString()
    {
        return IsLink
            ? $"ImgFrame(#{Index}, LINK→{LinkTarget?.Index.ToString() ?? "?"})"
            : $"ImgFrame(#{Index}, {Type}, {Compress}, {Width}x{Height}@{X},{Y}, {Length}B)";
    }
}
