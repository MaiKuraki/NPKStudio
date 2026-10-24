using NPKStudio.Core.Formats;

namespace NPKStudio.Desktop.Services;

/// <summary>
///     「实验性版本写回前确认一次」的会话记忆。
///     <para>
///         只记住"最近一次确认过的那个 IMG"：同一 IMG 本次会话内不再重复打扰，
///         切到别的 IMG 时 <see cref="Reset" />，重新问一次。
///     </para>
///     <para>
///         <b>为什么单独成类</b>：判定条件有四个（是否编辑过 / 版本等级 / 宿主能不能弹 / 是否已确认），
///         塞进 <c>MainWindowViewModel.SaveAsync</c> 里既难读也没法脱离窗口做单测。
///     </para>
/// </summary>
internal sealed class ExperimentalSaveConfirmation {
    private string? _confirmedKey;

    /// <summary>
    ///     本次保存是否需要弹一次确认。
    ///     <para>
    ///         自动化模式与无头测试宿主（<paramref name="canPrompt" /> 为 <c>false</c>）必须直接返回
    ///         <c>false</c> 走默认"继续"——弹窗没人点，会像关闭流程一样把进程卡死。
    ///     </para>
    /// </summary>
    /// <param name="imageKey">当前 IMG 的会话内唯一键（<c>MainWindowViewModel.CurrentImageKey</c>）。</param>
    /// <param name="version">当前 IMG 的版本。</param>
    /// <param name="hasEdits">是否真的被编辑过（没编辑过就没有写回降质的风险，不打扰用户）。</param>
    /// <param name="canPrompt">宿主是否允许弹窗。</param>
    /// <returns>是否需要弹确认。</returns>
    internal bool ShouldConfirmExperimentalSave(string? imageKey, ImgVersion version, bool hasEdits, bool canPrompt) {
        if (!canPrompt || !hasEdits) return false;
        if (VersionSupport.Of(version) != VersionSupportLevel.Experimental) return false;

        return imageKey is null || !string.Equals(_confirmedKey, imageKey, StringComparison.Ordinal);
    }

    /// <summary>记下"这个 IMG 已经确认过"（用户点了继续之后调用）。</summary>
    /// <param name="imageKey">当前 IMG 的会话内唯一键。</param>
    internal void MarkConfirmed(string? imageKey) => _confirmedKey = imageKey;

    /// <summary>切换 / 关闭 IMG 时重置，下次保存重新确认。</summary>
    internal void Reset() => _confirmedKey = null;
}
