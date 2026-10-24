using Avalonia;

using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.Localization;

namespace NPKStudio.Desktop.ViewModels.Channels;

/// <summary>
///     通道的<b>视图状态</b>：搜索过滤、当前图像（含「线性减淡」预览）、画布包围盒 / 帧偏移、
///     可见性批量操作、检查器快照。
///     <para>
///         <b>这些状态只影响"看"，不影响文件内容</b>：可见性不进撤销栈、不置脏；
///         搜索只改卡片视图；当前图像是解码结果 + 预览变换，从不写回模型。
///     </para>
///     <para>
///         <b>与通道的通信是直接方法调用，不是事件 / 委托</b>：改完状态后调
///         <see cref="FrameChannel.Notify" /> 与 <see cref="FrameChannel.Status" />，
///         都是 sealed 类型上的非虚调用（JIT 可去虚拟化并内联），零委托分配、零多播。
///     </para>
///     <para>
///         <b>不缓存可变引用</b>：帧集合与选中帧每次都从 <see cref="FrameChannel" /> 现取——
///         多选集合会被控件换实例（双向绑定），缓存一份就必然有过期引用。
///     </para>
/// </summary>
internal sealed class ChannelViewState
{
    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    private readonly FrameChannel _owner;
    private readonly DocumentSession _session;
    private readonly FrameCardPresenter _cards;
    private readonly CanvasProjection _projection = new();
    private readonly MostlyTransparentHint _transparentHint = new();

    private string _filterText = string.Empty;
    private RgbaImage? _currentImage;
    private RgbaImage? _rawImage;

    /// <summary>构造。</summary>
    /// <param name="owner">聚合根（回写属性与通知的唯一出口）。</param>
    /// <param name="session">文档会话（读当前 IMG）。</param>
    /// <param name="cards">卡片同步器（检查器快照要读它的 <c>Selection</c>）。</param>
    public ChannelViewState(FrameChannel owner, DocumentSession session, FrameCardPresenter cards) {
        _owner = owner;
        _session = session;
        _cards = cards;
    }

    /// <summary>原始解码结果（导出 / 换图 / 另存必须用它，绝不能被预览变换污染）。</summary>
    public RgbaImage? RawImage => _rawImage;

    // ── 搜索过滤 ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     帧列表的搜索关键字：按 <c>#序号</c> 与 <c>宽×高</c> 文案做不区分大小写的包含匹配；
    ///     空串表示不过滤。搜索框是窗口级的，但作用对象是当前通道的帧列表。
    /// </summary>
    public string FilterText {
        get => _filterText;
        set {
            var next = value ?? string.Empty;
            if (_filterText == next) return;

            _filterText = next;
            _owner.Notify(nameof(FilterText));
            _owner.Notify(nameof(FilteredFrames));
        }
    }

    /// <summary>按 <see cref="FilterText" /> 过滤后的帧视图（空关键字时就是全部帧）。</summary>
    public IReadOnlyList<FrameViewModel> FilteredFrames {
        get {
            var frames = _owner.Frames;
            var filter = _filterText.Trim();
            if (filter.Length == 0) return frames;

            var list = new List<FrameViewModel>();
            foreach (var frame in frames) {
                if (frame.IndexText.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    frame.SizeText.Contains(filter, StringComparison.OrdinalIgnoreCase)) {
                    list.Add(frame);
                }
            }

            return list;
        }
    }

    // ── 当前图像 ───────────────────────────────────────────────────────────────

    /// <summary>画布当前显示的帧像素。</summary>
    public RgbaImage? CurrentImage {
        get => _currentImage;
        private set {
            if (ReferenceEquals(_currentImage, value)) return;

            _currentImage = value;
            _owner.Notify(nameof(CurrentImage));
        }
    }

    /// <summary>关闭 / 解析失败时直接清空画布预览（不重新解码，也不动包围盒——那由调用方决定）。</summary>
    public void ClearCurrentImage() => CurrentImage = null;

    /// <summary>
    ///     按当前帧重新解码并套用画布效果。
    ///     <para>
    ///         <b>这是播放 tick 会走到的路径</b>：内部没有 LINQ、没有闭包、没有字符串拼接；
    ///         分配只来自解码本身（<c>Decode</c> 每次返回新数组）与「线性减淡」的副本。
    ///     </para>
    /// </summary>
    public void UpdateCurrentImage() {
        RgbaImage? raw = null;
        var image = _session.CurrentImg;
        var selected = _owner.SelectedFrame;
        if (image is not null && selected is not null) {
            try {
                raw = image.Decode(selected.Frame);
            } catch {
                raw = null;
            }
        }

        // 原始解码结果单独留一份引用：导出/替换/另存必须用它，绝不能被预览变换污染。
        _rawImage = raw;

        if (raw is null) {
            CurrentImage = null;
            UpdateFrameOffset();
            _owner.UpdateFrameCounter();
            return;
        }

        // 「线性减淡」改变贴图本身的 RGBA（逐字节复刻 pvfUtility 的 LinearBrun），
        // 结果不依赖背景；「原图」直接用解码结果。
        var linearDodge = _owner.UseLinearDodge;
        CurrentImage = linearDodge ? CreateLinearDodge(raw) : raw;

        if (linearDodge) {
            WarnIfMostlyTransparent(CurrentImage);
        }

        UpdateFrameOffset();
        _owner.UpdateFrameCounter();
    }

    /// <summary>
    ///     线性减淡下大面积变透明时提示一次（判定阈值与"只提示一次"的规则见 <see cref="MostlyTransparentHint" />）。
    /// </summary>
    private void WarnIfMostlyTransparent(RgbaImage? image) {
        if (_transparentHint.Check(image, _owner.Document.CurrentImageKey) is { } message) {
            _owner.Status = message;
        }
    }

    /// <summary>换 IMG 后允许重新提示一次「线减淡大面积透明」。</summary>
    public void ResetTransparentHint() => _transparentHint.Reset();

    /// <summary>
    ///     逐字节复刻 pvfUtility 的 <c>LinearBrun</c>（<c>Bitmaps.cs</c>）。
    ///     <para>
    ///         数组是 <b>BGRA</b>：<c>[i]=B、[i+1]=G、[i+2]=R、[i+3]=A</c>；
    ///         <c>b = max(B,G,R)</c>；<c>A = min(A, b)</c>；三个颜色通道各加 <c>255-b</c>。
    ///     </para>
    ///     <para>
    ///         刻意不做任何"优化"：<c>byte +=</c> 保持 unchecked 溢出语义、不跳过任何像素
    ///         （全透明像素也照写颜色），以保证与 pvfUtility 输出逐字节一致。
    ///     </para>
    /// </summary>
    /// <param name="source">解码后的帧（不会被修改）。</param>
    /// <returns>LinearBrun 之后的副本。</returns>
    public static RgbaImage CreateLinearDodge(RgbaImage source) {
        var result = new RgbaImage(source.Width, source.Height);
        Buffer.BlockCopy(source.Pixels, 0, result.Pixels, 0, source.Pixels.Length);

        var bytes = result.Pixels;
        for (var i = 0; i < bytes.Length; i += 4) {
            var brightest = Math.Max(bytes[i], Math.Max(bytes[i + 1], bytes[i + 2]));
            var lift = (byte)(255 - brightest);

            bytes[i + 3] = Math.Min(bytes[i + 3], brightest);
            bytes[i + 2] += lift;
            bytes[i + 1] += lift;
            bytes[i] += lift;
        }

        return result;
    }

    // ── 包围盒几何 ─────────────────────────────────────────────────────────────

    /// <summary>画布坐标系尺寸 = 当前 IMG 所有帧的<b>联合包围盒</b>（只统计有像素的帧，LINK 帧跳过）。</summary>
    public Size CanvasSize => _projection.CanvasSize;

    /// <summary>当前帧左上角在画布坐标系中的位置（= 帧坐标 - 包围盒左上角）。</summary>
    public Point FrameOffset => _projection.FrameOffset;

    /// <summary>画布坐标原点对应的角色坐标（标尺与状态栏显示角色坐标用）。</summary>
    public Point CanvasOrigin => _projection.Origin;

    /// <summary>
    ///     按 IMG 的全部帧重算联合包围盒（切换 IMG / 结构操作后一次）。
    ///     <para><b>LINK 帧不参与</b>：它自身没有像素（宽高为 0、X/Y 为 0），算进去会把包围盒错误地扩到原点。</para>
    /// </summary>
    /// <param name="image">当前 IMG；<c>null</c> 时归零。</param>
    public void ResetCanvasBounds(ImgFile? image) {
        if (!_projection.Reset(image)) return;

        _owner.Notify(nameof(CanvasSize));
        _owner.Notify(nameof(FrameOffset));
        _owner.Notify(nameof(CanvasOrigin));
    }

    /// <summary>把画布坐标更新为"帧在包围盒内的位置"；没有选中帧时归零。</summary>
    private void UpdateFrameOffset() {
        if (_projection.UpdateFrameOffset(_owner.SelectedFrame)) {
            _owner.Notify(nameof(FrameOffset));
        }
    }

    // ── 可见性（纯视图状态） ───────────────────────────────────────────────────

    /// <summary>当前被隐藏（播放时跳过）的帧数。</summary>
    public int HiddenFrameCount {
        get {
            var count = 0;
            foreach (var frame in _owner.Frames) {
                if (!frame.IsVisible) count++;
            }

            return count;
        }
    }

    /// <summary>是否存在隐藏帧：右键菜单「全部显示」的启停条件。</summary>
    public bool HasHiddenFrames => HiddenFrameCount > 0;

    /// <summary>当前帧是否已隐藏（画布仍显示它，只是播放会跳过）。</summary>
    public bool IsSelectedFrameHidden => _owner.SelectedFrame is { IsVisible: false };

    /// <summary>隐藏选区（多选时作用于全部选中帧）。纯视图状态：不置脏、不进撤销栈。</summary>
    public void HideSelected() => SetFramesVisible(VisibilityTargets(), false);

    /// <summary>把选区重新设为可见。</summary>
    public void ShowSelected() => SetFramesVisible(VisibilityTargets(), true);

    /// <summary>把整个 IMG 的帧全部设为可见。</summary>
    public void ShowAll() {
        var shown = 0;
        foreach (var frame in _owner.Frames) {
            if (frame.IsVisible) continue;

            frame.IsVisible = true;
            shown++;
        }

        if (shown == 0) return;

        NotifyFrameVisibility();
        _owner.Status = Loc.F("status.framesShown", shown);
    }

    /// <summary>快捷键 <c>H</c>：切换选区的可见性（只要还有可见帧就整体隐藏，否则整体显示）。</summary>
    public void ToggleSelected() {
        var targets = VisibilityTargets();
        if (targets.Count == 0) {
            _owner.Status = Loc["status.selectFrameFirst"];
            return;
        }

        var hide = false;
        foreach (var frame in targets) {
            if (!frame.IsVisible) continue;

            hide = true;
            break;
        }

        SetFramesVisible(targets, !hide);
    }

    /// <summary>自动化截图用：外部直接改过卡片可见性之后重算计数（正常交互走 Hide / Show / Toggle）。</summary>
    internal void RefreshVisibility() => NotifyFrameVisibility();

    /// <summary>可见性操作的作用范围：与删除 / 裁剪一致（多选取全部，否则取当前帧）。</summary>
    private List<FrameViewModel> VisibilityTargets() =>
        FrameEditOperations.SelectionForEdit(_owner.SelectedFrames, _owner.SelectedFrame);

    /// <summary>
    ///     批量改可见性。**只动卡片对象**，不碰 <see cref="ImgFile" />：文件字节、脏标记、撤销栈都不变。
    /// </summary>
    private void SetFramesVisible(IReadOnlyList<FrameViewModel> targets, bool visible) {
        if (targets.Count == 0) {
            _owner.Status = Loc["status.selectFrameFirst"];
            return;
        }

        var changed = 0;
        foreach (var frame in targets) {
            if (frame.IsVisible == visible) continue;

            frame.IsVisible = visible;
            changed++;
        }

        if (changed == 0) return;

        NotifyFrameVisibility();
        _owner.Status = visible
            ? Loc.F("status.framesShown", changed)
            : Loc.F("status.framesHidden", changed);

        // 当前帧被藏起来时单独说明：画布不会跳走，避免用户以为编辑对象丢了
        if (_owner.SelectedFrame is { IsVisible: false }) {
            _owner.Status = Loc["status.currentFrameHidden"];
        }
    }

    /// <summary>可见性变化后的统一广播（计数、菜单启停、帧计数里的「隐 N」）。</summary>
    private void NotifyFrameVisibility() {
        _owner.Notify(nameof(HiddenFrameCount));
        _owner.Notify(nameof(HasHiddenFrames));
        _owner.Notify(nameof(IsSelectedFrameHidden));
        _owner.UpdateFrameCounter();
    }

    // ── 检查器快照 ─────────────────────────────────────────────────────────────

    /// <summary>把选区推给检查器：多选只关心帧数，位置/尺寸控件会被禁用。</summary>
    public void SyncInspector() => _cards.SyncInspector(_owner.SelectedFrame);
}
