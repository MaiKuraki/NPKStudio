using Avalonia.Threading;

using NPKStudio.Desktop.Controls;
using NPKStudio.Desktop.Localization;
using NPKStudio.Desktop.Services;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>
///     画布"看的方式"：背景档位、效果（LINEARDODGE 等），以及按 IMG 记忆这两项的落盘。
///     <para>
///         从 <c>MainWindowViewModel</c> 抽出来的（原 <c>Backdrop</c> / <c>Effect</c> / 4 个派生文案属性 /
///         两个 Store / <c>PersistEffectSetting</c> / <c>RestoreEffect</c> / <c>SchedulePersist</c>）。
///     </para>
///     <para>
///         <b>状态变化不发通知</b>：由 VM 的转发层 raise（漏一个界面上某处就不刷新，所以测试覆盖了派生文案）。
///         记忆键（属于哪个 IMG）由调用方以参数传入，本类不反向依赖 VM。
///     </para>
/// </summary>
internal sealed class CanvasAppearance
{
    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    private readonly GraphicEffectStore _effects = GraphicEffectStore.Load();
    private readonly AppSettingsStore _settings;
    private DispatcherTimer? _persistTimer;
    private string[]? _effectNames;
    private bool _loading;

    /// <summary>构造。</summary>
    /// <param name="settings">全局设置（与播放器共用同一份，落盘由 VM 统一 Flush）。</param>
    public CanvasAppearance(AppSettingsStore settings) => _settings = settings;

    /// <summary>画布背景档位（只影响显示，切换不需要重算像素）。</summary>
    public CanvasBackground Backdrop { get; private set; } = CanvasBackground.Checker;

    /// <summary>画布效果。</summary>
    public CanvasEffect Effect { get; private set; }

    /// <summary>是否走线性减淡。</summary>
    public bool UseLinearDodge => Effect == CanvasEffect.LinearDodge;

    /// <summary>效果下拉选项（首次读取时缓存）。</summary>
    public IReadOnlyList<string> EffectNames => _effectNames ??= [Loc["effect.none"], Loc["effect.linearDodge"]];

    /// <summary>效果下拉当前项的文案。</summary>
    public string SelectedEffectName => Loc[EffectKey(Effect)];

    /// <summary>当前效果文案对应的枚举（下拉选中时用）。</summary>
    /// <param name="name">显示文案。</param>
    /// <param name="effect">解析出的效果。</param>
    /// <returns>是否匹配到已知项。</returns>
    public bool TryParseEffect(string name, out CanvasEffect effect)
    {
        foreach (var candidate in Enum.GetValues<CanvasEffect>())
        {
            if (Loc[EffectKey(candidate)] != name) continue;
            effect = candidate;
            return true;
        }

        effect = CanvasEffect.None;
        return false;
    }

    /// <summary>背景档位短名（播放条按钮直接显示）。</summary>
    public string BackdropShortName => Backdrop switch {
        CanvasBackground.CheckerLight => Loc["backdrop.short.checkerLight"],
        CanvasBackground.CheckerDark => Loc["backdrop.short.checkerDark"],
        CanvasBackground.Black => Loc["backdrop.short.black"],
        CanvasBackground.Dark => Loc["backdrop.short.dark"],
        CanvasBackground.Light => Loc["backdrop.short.light"],
        CanvasBackground.Green => Loc["backdrop.short.green"],
        _ => Loc["backdrop.short.checker"]
    };

    /// <summary>背景档位完整名（提示语）。</summary>
    public string BackdropTooltip => Backdrop switch {
        CanvasBackground.CheckerLight => Loc["canvas.bg.checkerLight"],
        CanvasBackground.CheckerDark => Loc["canvas.bg.checkerDark"],
        CanvasBackground.Black => Loc["canvas.bg.black"],
        CanvasBackground.Dark => Loc["canvas.bg.dark"],
        CanvasBackground.Light => Loc["canvas.bg.light"],
        CanvasBackground.Green => Loc["canvas.bg.green"],
        _ => Loc["canvas.bg.checker"]
    };

    /// <summary>设置背景档位。</summary>
    /// <param name="value">新档位。</param>
    /// <returns>是否发生变化。</returns>
    public bool SetBackdrop(CanvasBackground value)
    {
        if (Backdrop == value) return false;
        Backdrop = value;
        return true;
    }

    /// <summary>
    ///     设置效果并按 IMG 记忆（防抖 500ms 落盘）。
    /// </summary>
    /// <param name="value">新效果。</param>
    /// <param name="imageKey">当前 IMG 的记忆键；<c>null</c> 时不记录。</param>
    /// <returns>是否发生变化。</returns>
    public bool SetEffect(CanvasEffect value, string? imageKey)
    {
        if (Effect == value) return false;

        Effect = value;
        if (!_loading && imageKey is not null)
        {
            _effects.Set(imageKey, value);
            SchedulePersist();
        }

        return true;
    }

    /// <summary>
    ///     切换 IMG 时恢复它记忆的效果（恢复期间不写回）。
    /// </summary>
    /// <param name="imageKey">当前 IMG 的记忆键。</param>
    /// <returns>恢复出的效果。</returns>
    public CanvasEffect Restore(string? imageKey)
    {
        var effect = _effects.Get(imageKey, CanvasEffect.None);
        _loading = true;
        try
        {
            Effect = effect;
        }
        finally
        {
            _loading = false;
        }

        return effect;
    }

    /// <summary>语言切换等场景下清掉文案缓存。</summary>
    public void InvalidateNames() => _effectNames = null;

    /// <summary>立即落盘（关窗口时调用，避免防抖没触发就退出）。</summary>
    public void Flush()
    {
        _persistTimer?.Stop();
        _persistTimer = null;
        _effects.Save();
        _settings.Save();
    }

    private void SchedulePersist()
    {
        _persistTimer?.Stop();
        _persistTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _persistTimer.Tick += (_, _) => {
            _persistTimer?.Stop();
            _persistTimer = null;
            _effects.Save();
        };
        _persistTimer.Start();
    }

    private static string EffectKey(CanvasEffect effect) => effect == CanvasEffect.LinearDodge
        ? "effect.linearDodge"
        : "effect.none";
}
