namespace NPKStudio.Core.Diagnostics;

/// <summary>
///     极简日志抽象。Core 不引入任何日志库，只依赖此接口；UI 层可替换为文件/调试窗口实现。
///     <para>约定：<c>Error</c> 必须携带 <see cref="Exception" />（若确有异常），<c>Warn</c> 用于"可继续但需注意"的情形。</para>
/// </summary>
public interface ILog
{
    /// <summary>
    ///     记录一般信息。
    /// </summary>
    /// <param name="message">日志正文。</param>
    /// <param name="exception">相关异常，可为 <c>null</c>。</param>
    void Info(string message, Exception? exception = null);

    /// <summary>
    ///     记录警告。
    /// </summary>
    /// <param name="message">日志正文。</param>
    /// <param name="exception">相关异常，可为 <c>null</c>。</param>
    void Warn(string message, Exception? exception = null);

    /// <summary>
    ///     记录错误。
    /// </summary>
    /// <param name="message">日志正文。</param>
    /// <param name="exception">相关异常，可为 <c>null</c>。</param>
    void Error(string message, Exception? exception = null);
}
