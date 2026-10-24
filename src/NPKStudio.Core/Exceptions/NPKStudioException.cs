namespace NPKStudio.Core.Exceptions;

/// <summary>
///     本库所有受控异常的基类。Core 不把裸 <see cref="System.IO.IOException" /> 交给 UI，
///     UI 层只需捕获本类型即可给出可读文案。
/// </summary>
public class NPKStudioException : Exception
{
    /// <summary>
    ///     使用默认消息初始化 <see cref="NPKStudioException" />。
    /// </summary>
    public NPKStudioException()
    {
    }

    /// <summary>
    ///     使用指定消息初始化 <see cref="NPKStudioException" />。
    /// </summary>
    /// <param name="message">面向用户的错误描述。</param>
    public NPKStudioException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     使用指定消息与内部异常初始化 <see cref="NPKStudioException" />。
    /// </summary>
    /// <param name="message">面向用户的错误描述。</param>
    /// <param name="innerException">造成本次失败的底层异常。</param>
    public NPKStudioException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
