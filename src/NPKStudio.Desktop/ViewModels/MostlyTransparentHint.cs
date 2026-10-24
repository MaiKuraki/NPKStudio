using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.Localization;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>
///     「线性减淡把这一帧打穿了」的一次性提示。
///     <para>
///         线性减淡的纯黑底会被当成透明（<c>a' = min(a, max(r,g,b))</c>），当一帧 ≥75% 的像素因此变透明时，
///         用户很容易误以为"图没显示"。这里只<b>算比例 + 出文案</b>，同一张 IMG 内只提示一次；
///         不自动切效果、不弹窗、不改算法。
///     </para>
///     <para>
///         阈值取 <b>0.75</b> 而不是 0.9：实测 <c>vip3back.img</c> 的黑底占 <b>77.6%</b>，
///         0.9 会让这个"看起来图没了"的典型案例不提示；而正常帧（dod#001）只有 27%，不会被误伤。
///     </para>
/// </summary>
internal sealed class MostlyTransparentHint
{
    private const double Threshold = 0.75;

    /// <summary>已经提示过的 IMG 记忆键；同一 IMG 只提示一次。</summary>
    private string? _warnedFor;

    /// <summary>换 IMG 时调用，允许重新提示一次。</summary>
    public void Reset() => _warnedFor = null;

    /// <summary>
    ///     检查是否需要提示。
    /// </summary>
    /// <param name="image">已经过线性减淡的画布图像。</param>
    /// <param name="imageKey">当前 IMG 的记忆键（用于"只提示一次"）。</param>
    /// <returns>要显示的提示文案；不需要提示时返回 <c>null</c>。</returns>
    public string? Check(RgbaImage? image, string? imageKey) {
        if (image is null) {
            return null;
        }

        var pixels = image.Pixels;
        var total = pixels.Length / 4;

        // 太小的帧（如 DNF 的 1×1 占位帧）"100% 透明"是废话，不提示
        if (total < 16) {
            return null;
        }

        // 只扫 alpha 字节（步长 4）：1 万像素帧约 10µs，最大帧约 1.4ms，且只在切帧时一次
        var transparent = 0;
        for (var i = 3; i < pixels.Length; i += 4) {
            if (pixels[i] == 0) {
                transparent++;
            }
        }

        var ratio = (double)transparent / total;
        if (ratio < Threshold) {
            return null;
        }

        if (string.Equals(_warnedFor, imageKey, StringComparison.Ordinal)) {
            return null;
        }

        _warnedFor = imageKey;
        return TranslationCatalog.Instance.F("status.lineardodgeTransparent", (int)Math.Round(ratio * 100));
    }
}
