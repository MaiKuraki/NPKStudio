using NPKStudio.Core.Formats;
using NPKStudio.Desktop.Localization;
using NPKStudio.Desktop.Services;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>
///     帧检查器（右栏第三段）的状态：当前帧的位置 / 尺寸的可编辑视图，以及只读信息文案。
///     <para>
///         <b>不持有 <see cref="MainWindowViewModel" /></b>：只注入 <see cref="DocumentSession" />，
///         提交结果通过 <see cref="StatusMessage" /> 与 <see cref="GeometryVersion" /> 两个属性向外广播，
///         由 VM 订阅后转发到状态栏 / 重算画布——避免在两端各挂一个委托字段互相引用。
///     </para>
///     <para>
///         <b>为什么是 <c>decimal?</c></b>：Avalonia 11 的 <c>NumericUpDown.Value</c> 是 <c>decimal?</c>
///         （不是 <c>double?</c>），写成 <c>double?</c> 会走隐式类型转换、编译绑定报错。
///     </para>
/// </summary>
internal sealed class FrameInspector : ViewModelBase {
    /// <summary>与 Core 的帧尺寸护栏一致（<c>ImgHandlerBase.MaxDimension</c>，那边是 internal，这里不能引用）。</summary>
    private const int MaxDimension = 8192;

    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    private readonly DocumentSession _session;
    private ImgFrame? _frame;
    private int _selectionCount;
    private bool _pushing;
    private decimal? _x;
    private decimal? _y;
    private decimal? _width;
    private decimal? _height;
    private bool _xInvalid;
    private bool _yInvalid;
    private bool _widthInvalid;
    private bool _heightInvalid;
    private string _statusMessage = string.Empty;
    private int _geometryVersion;
    private bool _lastCommitResizedPixels;

    public FrameInspector(DocumentSession session) => _session = session;

    /// <summary>是否有当前帧（决定整块检查器是否显示）。</summary>
    public bool HasSelection => _frame is not null;

    /// <summary>是否只有一帧被选中（多选时位置/尺寸控件只读）。</summary>
    public bool IsSingle => _frame is not null && _selectionCount <= 1;

    /// <summary>多选时隐藏"尺寸 / 格式"这几个只针对单帧的字段。</summary>
    public bool HasFrameMeta => IsSingle;

    /// <summary>
    ///     当前版本是否"仅透传"（v7/v8/v9 与非 IMG 条目）：没有帧、也不允许任何帧编辑。
    ///     <para>这种版本下 <see cref="ImgFile" /> 的编辑方法本来就会抛 <c>NotSupportedException</c>；
    ///     这里置灰是为了"不让用户点了才发现不行"，不是替代异常。</para>
    /// </summary>
    public bool IsVersionPassthrough => CurrentVersion is { } version
                                        && VersionSupport.Of(version) == VersionSupportLevel.Passthrough;

    /// <summary>当前版本是否"实验性"（v4/v5/v6）：能读写，但写回前要提示风险。</summary>
    public bool IsVersionExperimental => CurrentVersion is { } version
                                         && VersionSupport.Of(version) == VersionSupportLevel.Experimental;

    /// <summary>位置输入框可改：单选且不是透传版本。</summary>
    public bool CanEditPosition => IsSingle && !IsVersionPassthrough;

    /// <summary>尺寸可改：单选、不是 LINK 帧（LINK 帧没有自己的像素，Core 会抛异常）、不是透传版本。</summary>
    public bool CanResize => IsSingle && _frame is { IsLink: false } && !IsVersionPassthrough;

    public bool CanInsert => _frame is not null && !IsVersionPassthrough;

    public bool CanDelete => _frame is not null && !IsVersionPassthrough;

    public bool CanMoveUp => _frame is { } frame && frame.Index > 0 && !IsVersionPassthrough;

    public bool CanMoveDown => _frame is { } frame
                               && _session.CurrentImg is { } img
                               && frame.Index < img.Frames.Count - 1
                               && !IsVersionPassthrough;

    /// <summary>标题：单选为 <c>#003</c>，多选为「已选 N 帧」。</summary>
    public string TitleText => _frame is not { } frame
        ? string.Empty
        : _selectionCount > 1
            ? Loc.F("inspector.multi", _selectionCount)
            : $"#{frame.Index:D3}";

    /// <summary>标题旁的尺寸文案，形如 <c>96 × 112</c>。</summary>
    public string SizeText => IsSingle && _frame is { } frame ? $"{frame.Width} × {frame.Height}" : string.Empty;

    /// <summary>
    ///     只读 chip：版本 + 像素格式 + 压缩方式，形如 <c>v5 · 实验性 · 线性 1555 · ZLIB</c>。
    ///     <para>已验证版本只显示版本号（<c>v2</c>），不额外标"已验证"——正常状态不该有任何警示。</para>
    /// </summary>
    public string MetaText {
        get {
            if (_frame is not { } frame) return string.Empty;

            var format = $"{FormatName(frame.Type)} · {CompressName(frame.Compress)}";
            return VersionBadge is { Length: > 0 } badge ? $"{badge} · {format}" : format;
        }
    }

    /// <summary>
    ///     chip 的提示语：实验性 / 仅透传版本解释风险，已验证版本为 <c>null</c>（不显示提示框）。
    /// </summary>
    public string? VersionTip => CurrentVersion is { } version
        ? VersionSupport.Of(version) switch {
            VersionSupportLevel.Experimental => Loc["version.tip.experimental"],
            VersionSupportLevel.Passthrough => Loc["version.tip.passthrough"],
            _ => null
        }
        : null;

    /// <summary>底部只读补充信息：帧域，LINK 帧追加目标序号。</summary>
    public string FooterText {
        get {
            if (_frame is not { } frame) return string.Empty;

            return frame.LinkTarget is { } target
                ? Loc.F("inspector.frameAreaLink", frame.FrameWidth, frame.FrameHeight, $"#{target.Index:D3}")
                : Loc.F("inspector.frameArea", frame.FrameWidth, frame.FrameHeight);
        }
    }

    /// <summary>最近一次提交的结果文案（VM 转发到状态栏）；空串表示没有新文案。</summary>
    public string StatusMessage {
        get => _statusMessage;
        private set {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>每次成功改动几何都 +1；VM 据此重算画布包围盒并重绘当前帧。</summary>
    public int GeometryVersion {
        get => _geometryVersion;
        private set {
            _geometryVersion = value;
            OnPropertyChanged();
        }
    }

    /// <summary>最近一次成功提交是否改变了像素尺寸（VM 据此决定要不要重建这一帧的缩略图）。</summary>
    public bool LastCommitResizedPixels {
        get => _lastCommitResizedPixels;
        private set {
            if (_lastCommitResizedPixels == value) return;
            _lastCommitResizedPixels = value;
            OnPropertyChanged();
        }
    }

    public decimal? X {
        get => _x;
        set {
            _x = value;
            OnPropertyChanged();
            if (!_pushing) ApplyPosition();
        }
    }

    public decimal? Y {
        get => _y;
        set {
            _y = value;
            OnPropertyChanged();
            if (!_pushing) ApplyPosition();
        }
    }

    public decimal? Width {
        get => _width;
        set {
            _width = value;
            OnPropertyChanged();
            if (!_pushing) ApplySize();
        }
    }

    public decimal? Height {
        get => _height;
        set {
            _height = value;
            OnPropertyChanged();
            if (!_pushing) ApplySize();
        }
    }

    public bool XInvalid => _xInvalid;

    public bool YInvalid => _yInvalid;

    public bool WidthInvalid => _widthInvalid;

    public bool HeightInvalid => _heightInvalid;

    /// <summary>把选区同步进来：<paramref name="primary" /> 是"当前帧"，<paramref name="selectionCount" /> 是选区帧数。</summary>
    public void Show(FrameViewModel? primary, int selectionCount) {
        _frame = primary?.Frame;
        _selectionCount = _frame is null ? 0 : Math.Max(1, selectionCount);

        _pushing = true;
        try {
            X = _frame?.X;
            Y = _frame?.Y;
            Width = _frame?.Width;
            Height = _frame?.Height;
        } finally {
            _pushing = false;
        }

        MarkInvalid();
        Refresh();
    }

    private void ApplyPosition() {
        if (_frame is not { } frame || _session.CurrentImg is not { } img || !IsSingle || IsVersionPassthrough) return;

        LastCommitResizedPixels = false;
        var okX = TryToInt(_x, out var x);
        var okY = TryToInt(_y, out var y);
        MarkInvalid(x: !okX, y: !okY);

        if (!okX || !okY) {
            StatusMessage = Loc["status.frameCoordInvalid"];
            return;
        }

        try {
            var changed = img.SetFramePosition(frame, x, y);
            StatusMessage = Loc.F("status.framePositionSet", $"#{frame.Index:D3}", x, y);

            // 没变就不要再触发一次重绘（NumericUpDown 失焦也会回写同值）
            if (changed) GeometryVersion++;
        } catch (Exception ex) {
            StatusMessage = Loc.F("status.frameEditFailed", ex.Message);
        }
    }

    private void ApplySize() {
        if (_frame is not { } frame || _session.CurrentImg is not { } img || !IsSingle || IsVersionPassthrough) return;

        LastCommitResizedPixels = false;
        var okWidth = TryToInt(_width, out var width) && width is >= 1 and <= MaxDimension;
        var okHeight = TryToInt(_height, out var height) && height is >= 1 and <= MaxDimension;
        MarkInvalid(width: !okWidth, height: !okHeight);

        if (!okWidth || !okHeight) {
            StatusMessage = Loc.F("status.frameSizeInvalid", MaxDimension);
            return;
        }

        if (frame.IsLink) {
            StatusMessage = Loc["status.frameLinkNoResize"];
            return;
        }

        try {
            var changed = img.ResizeFrame(frame, width, height);
            StatusMessage = Loc.F("status.frameResized", $"#{frame.Index:D3}", width, height);

            if (changed) {
                LastCommitResizedPixels = true;
                GeometryVersion++;
                Refresh();
            }
        } catch (Exception ex) {
            StatusMessage = Loc.F("status.frameEditFailed", ex.Message);
        }
    }

    /// <summary>只读文案（序号 / 尺寸 / 格式 / 帧域 / 移动按钮可用性）整体刷新。</summary>
    private void Refresh() {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSingle));
        OnPropertyChanged(nameof(HasFrameMeta));
        OnPropertyChanged(nameof(CanEditPosition));
        OnPropertyChanged(nameof(CanResize));
        OnPropertyChanged(nameof(CanInsert));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(IsVersionExperimental));
        OnPropertyChanged(nameof(IsVersionPassthrough));
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(VersionTip));
        OnPropertyChanged(nameof(FooterText));
    }

    private void MarkInvalid(bool x = false, bool y = false, bool width = false, bool height = false) {
        SetFlag(ref _xInvalid, x, nameof(XInvalid));
        SetFlag(ref _yInvalid, y, nameof(YInvalid));
        SetFlag(ref _widthInvalid, width, nameof(WidthInvalid));
        SetFlag(ref _heightInvalid, height, nameof(HeightInvalid));
    }

    private void SetFlag(ref bool field, bool value, string name) {
        if (field == value) return;
        field = value;
        OnPropertyChanged(name);
    }

    private static bool TryToInt(decimal? value, out int result) {
        result = 0;
        if (value is not { } raw) return false;

        var rounded = decimal.Round(raw, MidpointRounding.AwayFromZero);
        if (rounded < int.MinValue || rounded > int.MaxValue) return false;

        result = (int)rounded;
        return true;
    }

    /// <summary>当前 IMG 的版本（检查器不持有 <c>ImgFile</c>，版本从会话里取）。</summary>
    private ImgVersion? CurrentVersion => _session.CurrentImg?.Version;

    /// <summary>版本徽标：<c>v2</c>；实验性 / 仅透传追加等级名（<c>v5 · 实验性</c>）。</summary>
    private string VersionBadge => CurrentVersion is not { } version
        ? string.Empty
        : VersionSupport.Of(version) == VersionSupportLevel.Verified
            ? $"v{(int)version}"
            : $"v{(int)version} · {Loc[VersionSupport.BadgeKey(VersionSupport.Of(version))]}";

    private static string FormatName(ColorBits type) => type switch {
        ColorBits.ARGB_1555 => Loc["bit.argb1555"],
        ColorBits.ARGB_4444 => Loc["bit.argb4444"],
        ColorBits.ARGB_8888 => Loc["bit.argb8888"],
        ColorBits.LINK => Loc["bit.link"],
        ColorBits.DXT_1 => "DXT1",
        ColorBits.DXT_3 => "DXT3",
        ColorBits.DXT_5 => "DXT5",
        _ => Loc["bit.unknown"]
    };

    private static string CompressName(CompressMode mode) => mode switch {
        CompressMode.NONE => Loc["compress.none"],
        CompressMode.ZLIB => "ZLIB",
        CompressMode.DDS_ZLIB => "DDS·ZLIB",
        _ => Loc["compress.unknown"]
    };
}
