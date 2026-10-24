namespace NPKStudio.Core.Exceptions;

/// <summary>
///     IMG 文件或其中的帧数据不合法（长度不足、压缩流损坏、像素格式不支持等）。
/// </summary>
public sealed class ImgFormatException : NPKStudioException
{
    /// <summary>
    ///     使用默认消息初始化 <see cref="ImgFormatException" />。
    /// </summary>
    public ImgFormatException()
        : base("IMG 文件结构不合法。")
    {
    }

    /// <summary>
    ///     使用指定消息初始化 <see cref="ImgFormatException" />。
    /// </summary>
    /// <param name="message">面向用户的错误描述。</param>
    public ImgFormatException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     使用指定消息与内部异常初始化 <see cref="ImgFormatException" />。
    /// </summary>
    /// <param name="message">面向用户的错误描述。</param>
    /// <param name="innerException">造成本次失败的底层异常。</param>
    public ImgFormatException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
