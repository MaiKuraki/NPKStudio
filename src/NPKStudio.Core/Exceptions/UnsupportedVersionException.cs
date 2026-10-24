using NPKStudio.Core.Formats;

namespace NPKStudio.Core.Exceptions;

/// <summary>
///     遇到当前构建不支持的 IMG 版本（如 v7/v8/v9 或未知版本号）时抛出。
/// </summary>
public sealed class UnsupportedVersionException : NPKStudioException
{
    /// <summary>
    ///     使用指定消息初始化 <see cref="UnsupportedVersionException" />。
    /// </summary>
    /// <param name="message">面向用户的错误描述。</param>
    public UnsupportedVersionException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     使用指定消息与内部异常初始化 <see cref="UnsupportedVersionException" />。
    /// </summary>
    /// <param name="message">面向用户的错误描述。</param>
    /// <param name="innerException">造成本次失败的底层异常。</param>
    public UnsupportedVersionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     使用不支持的版本枚举值初始化 <see cref="UnsupportedVersionException" />。
    /// </summary>
    /// <param name="version">读取到的版本号。</param>
    public UnsupportedVersionException(ImgVersion version)
        : base($"不支持的 IMG 版本：{version}（0x{(int)version:X2}）。")
    {
        Version = version;
    }

    /// <summary>
    ///     读取到的版本号，若由字符串消息构造则为 <see cref="Formats.ImgVersion.Other" />。
    /// </summary>
    public ImgVersion Version { get; } = ImgVersion.Other;
}
