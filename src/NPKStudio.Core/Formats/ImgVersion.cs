namespace NPKStudio.Core.Formats;

/// <summary>
///     IMG 头中的版本号。<para>枚举值与旧实现 <c>NPKStudio.Core.Handle.ImgVersion</c> 逐值一致，不得重排。</para>
/// </summary>
public enum ImgVersion
{
    /// <summary>未知/非 IMG（如 .ogg 音频条目）。</summary>
    Other = 0x00,

    /// <summary>v1：头部为 <c>"Neople Image File\0"</c>，帧数据内联在索引流中。</summary>
    Ver1 = 0x01,

    /// <summary>v2：基准版本，索引区与数据区分离。</summary>
    Ver2 = 0x02,

    /// <summary>v4：单张调色板 + 1 字节/像素索引。</summary>
    Ver4 = 0x04,

    /// <summary>v5：DDS 纹理页（DXT1/3/5）。</summary>
    Ver5 = 0x05,

    /// <summary>v6：多张色表 + 1 字节/像素索引。</summary>
    Ver6 = 0x06,

    /// <summary>v7：本库不支持（保留原值）。</summary>
    Ver7 = 0x07,

    /// <summary>v8：本库不支持（保留原值）。</summary>
    Ver8 = 0x08,

    /// <summary>v9：本库不支持（保留原值）。</summary>
    Ver9 = 0x09
}
