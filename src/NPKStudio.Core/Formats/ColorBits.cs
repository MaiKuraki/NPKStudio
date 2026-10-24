namespace NPKStudio.Core.Formats;

/// <summary>
///     帧的像素格式（索引区首字段）。<para>枚举值与旧实现 <c>NPKStudio.Core.Model.ColorBits</c> 逐值一致，不得重排。</para>
/// </summary>
public enum ColorBits
{
    /// <summary>未知格式。</summary>
    UNKNOWN = 0x00,

    /// <summary>ARGB1555，每像素 2 字节（a:1 r:5 g:5 b:5，小端）。</summary>
    ARGB_1555 = 0x0E,

    /// <summary>ARGB4444，每像素 2 字节（a:4 r:4 g:4 b:4，小端）。</summary>
    ARGB_4444 = 0x0F,

    /// <summary>ARGB8888，每像素 4 字节，直接 BGRA 字节序。</summary>
    ARGB_8888 = 0x10,

    /// <summary>链接帧，指向同文件内的另一帧，不占数据区。</summary>
    LINK = 0x11,

    /// <summary>DXT1 块压缩（仅 v5）。</summary>
    DXT_1 = 0x12,

    /// <summary>DXT3 块压缩（仅 v5）。</summary>
    DXT_3 = 0x13,

    /// <summary>DXT5 块压缩（仅 v5）。</summary>
    DXT_5 = 0x14
}
