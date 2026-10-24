namespace NPKStudio.Core.Exceptions;

/// <summary>
///     NPK 容器结构不合法（魔数错误、条目表越界、SHA256 校验失败等）。
/// </summary>
public sealed class NpkFormatException : NPKStudioException
{
    /// <summary>
    ///     使用默认消息初始化 <see cref="NpkFormatException" />。
    /// </summary>
    public NpkFormatException()
        : base("NPK 文件结构不合法。")
    {
    }

    /// <summary>
    ///     使用指定消息初始化 <see cref="NpkFormatException" />。
    /// </summary>
    /// <param name="message">面向用户的错误描述。</param>
    public NpkFormatException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     使用指定消息与内部异常初始化 <see cref="NpkFormatException" />。
    /// </summary>
    /// <param name="message">面向用户的错误描述。</param>
    /// <param name="innerException">造成本次失败的底层异常。</param>
    public NpkFormatException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
