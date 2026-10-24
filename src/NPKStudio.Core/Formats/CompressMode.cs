namespace NPKStudio.Core.Formats;

/// <summary>
///     帧数据的压缩方式（索引区第二字段）。<para>枚举值与旧实现 <c>NPKStudio.Core.Model.CompressMode</c> 逐值一致，不得重排。</para>
/// </summary>
public enum CompressMode
{
    /// <summary>未知压缩方式（保留原值）。</summary>
    UNKNOWN = 0x01,

    /// <summary>未压缩，数据长度 = 宽 × 高 × 每像素字节数。</summary>
    NONE = 0x05,

    /// <summary>zlib 压缩。</summary>
    ZLIB = 0x06,

    /// <summary>v5 DDS 纹理页使用的 zlib 压缩。</summary>
    DDS_ZLIB = 0x07
}
