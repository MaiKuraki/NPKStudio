using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.Localization;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>右面板的一张帧卡片（帧数据的展示包装）。</summary>
public sealed class FrameViewModel : ViewModelBase {
    private RgbaImage? _thumbnail;
    private bool _isCurrent;
    private bool _isVisible = true;
    private FrameChangeKind _changeKind;

    public FrameViewModel(ImgFrame frame) => Frame = frame;

    /// <summary>底层帧数据。</summary>
    public ImgFrame Frame { get; }

    /// <summary>帧序号文案，形如 <c>#001</c>。</summary>
    public string IndexText => $"#{Frame.Index:D3}";

    /// <summary>尺寸文案，形如 <c>96×112</c>。</summary>
    public string SizeText => $"{Frame.Width}×{Frame.Height}";

    /// <summary>缩略图（可能为 <c>null</c>，表示尚未解码或解码失败）。</summary>
    public RgbaImage? Thumbnail {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    /// <summary>
    ///     帧数据被外部改动（增删 / 移动 / 改尺寸）后刷新派生文案。
    ///     <para>卡片对象会被结构操作<b>复用</b>（缩略图不重新解码），所以序号与尺寸必须显式通知，否则卡片会停在旧值。</para>
    /// </summary>
    public void RefreshMetrics() {
        OnPropertyChanged(nameof(IndexText));
        OnPropertyChanged(nameof(SizeText));
    }

    /// <summary>
    ///     是否为画布当前显示的帧（播放时高亮跟随移动）。
    ///     <para>只由 ViewModel 对"旧帧 / 新帧"两个对象设置，<b>不遍历整个集合</b>。</para>
    /// </summary>
    public bool IsCurrent {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    /// <summary>
    ///     该帧是否参与播放（<b>纯视图状态</b>：默认为 <c>true</c>）。
    ///     <para>
    ///         关掉只是"播放时跳过 + 缩略图变暗"，<b>不改 <see cref="ImgFrame" /> 任何字段</b>：
    ///         不置脏、不进撤销栈、不参与编码，导出 PNG / GIF 与保存 NPK 完全不受影响。
    ///         隐藏状态也不持久化——重新打开文件就全部恢复可见。
    ///     </para>
    /// </summary>
    public bool IsVisible {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    /// <summary>
    ///     相对基线快照的变更集合（**纯视图状态**：不写文件、不进撤销栈、不置脏）。
    ///     <para>只由 <see cref="FrameChangeTracker" /> 按基线重算后写入，不在编辑入口手工累加。</para>
    /// </summary>
    internal FrameChangeKind ChangeKind {
        get => _changeKind;
        set {
            if (!SetProperty(ref _changeKind, value)) return;

            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(ShowInsertedBadge));
            OnPropertyChanged(nameof(ShowPixelsBadge));
            OnPropertyChanged(nameof(ShowGeometryBadge));
            OnPropertyChanged(nameof(ShowMovedBadge));
            OnPropertyChanged(nameof(ChangeTooltip));
        }
    }

    /// <summary>是否有任何变更（卡片描边与 tooltip 的开关）。</summary>
    public bool HasChanges => _changeKind != FrameChangeKind.None;

    /// <summary>
    ///     角标优先级：<c>新增 &gt; 替换 &gt; 尺寸 &gt; 移动</c>，一帧只显示一个，其余看 tooltip。
    ///     理由：卡片只有 96px 宽，四个角标会糊成一片；"新插的帧"最能解释"这帧为什么在这儿"。
    /// </summary>
    public bool ShowInsertedBadge => (_changeKind & FrameChangeKind.Inserted) != 0;

    /// <summary>替换素材角标。</summary>
    public bool ShowPixelsBadge => (_changeKind & FrameChangeKind.Pixels) != 0 && !ShowInsertedBadge;

    /// <summary>改尺寸角标。</summary>
    public bool ShowGeometryBadge =>
        (_changeKind & FrameChangeKind.Geometry) != 0 &&
        (_changeKind & (FrameChangeKind.Inserted | FrameChangeKind.Pixels)) == 0;

    /// <summary>移动角标（只有在没有其它标记时才单独显示）。</summary>
    public bool ShowMovedBadge =>
        (_changeKind & FrameChangeKind.Moved) != 0 &&
        (_changeKind & (FrameChangeKind.Inserted | FrameChangeKind.Pixels | FrameChangeKind.Geometry)) == 0;

    /// <summary>悬停提示：把全部标记列全；无变更时返回 <c>null</c>（不弹空提示框）。</summary>
    public string? ChangeTooltip {
        get {
            if (_changeKind == FrameChangeKind.None) return null;

            var loc = TranslationCatalog.Instance;
            var parts = new List<string>(4);
            if ((_changeKind & FrameChangeKind.Pixels) != 0) parts.Add(loc["frame.change.pixels"]);
            if ((_changeKind & FrameChangeKind.Inserted) != 0) parts.Add(loc["frame.change.inserted"]);
            if ((_changeKind & FrameChangeKind.Geometry) != 0) parts.Add(loc["frame.change.geometry"]);
            if ((_changeKind & FrameChangeKind.Moved) != 0) parts.Add(loc["frame.change.moved"]);

            return string.Join(" / ", parts);
        }
    }
}
