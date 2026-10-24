using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats;

/// <summary>
///     v5 帧的纹理引用：指向该 IMG 的一张 DDS 纹理页，并给出帧在该纹理页中的矩形。
///     <para>索引区字节布局（帧索引的扩展部分，7 × int32）：
///     <c>unknown, textureIndex, leftUpX, leftUpY, rightDownX, rightDownY, rotate</c>。</para>
/// </summary>
public sealed class TextureRef
{
    /// <summary>
    ///     索引区中紧跟帧几何之后的一个未知字段（原样读写保留，语义未知）。
    /// </summary>
    public int Unknown;

    /// <summary>
    ///     纹理页下标（0 基），指向 v5 纹理表。
    /// </summary>
    public int TextureIndex;

    /// <summary>
    ///     帧在纹理页中的矩形，由 <c>leftUpX/leftUpY/rightDownX/rightDownY</c> 换算得到。
    /// </summary>
    public PixelBounds Rect;

    /// <summary>
    ///     旧实现的 <c>top</c> 字段：历史注释为"非 0 时解码后需 Rotate90FlipXY"。
    ///     <para>
    ///         新实现<b>不执行</b>任何旋转：原旋转是为掩盖解码器行列颠倒（stride 用 height、x/y 互换）
    ///         而打的补丁（D-18），规范语义下 DXT 块数据本身就是正立的。
    ///         但本字段<b>必须原样读写保留</b>——"旋转不用"与"字段不能丢"是两件事，
    ///         丢掉它会让 v5 索引区的字节往返直接崩掉。
    ///     </para>
    /// </summary>
    public int Rotate;

    /// <summary>
    ///     返回可读文本，便于日志定位。
    /// </summary>
    public override string ToString()
    {
        return $"TextureRef(TextureIndex={TextureIndex}, Rect={Rect}, Rotate={Rotate}, Unknown={Unknown})";
    }
}
