using System.Collections.ObjectModel;

using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.Localization;
using NPKStudio.Desktop.Services;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>一次撤销 / 重做的结果：是否生效、恢复后应选中的帧、状态栏文案。</summary>
/// <param name="Applied">是否真的改动了模型（栈为空时为 <c>false</c>）。</param>
/// <param name="Select">恢复后应选中的底层帧；<c>null</c> 表示列表为空或保持原样。</param>
/// <param name="Status">已经格式化好的状态栏文案。</param>
internal readonly record struct EditStep(bool Applied, ImgFrame? Select, string Status);

/// <summary>
///     帧编辑的编排层：撤销 / 重做、裁剪、换图、批量导出与 GIF。
///     <para>
///         <b>为什么单独成类</b>：这些操作共用同一套前置条件（有 IMG、有选区、要置脏、要登记历史）
///         和同一套收尾（状态栏文案、缩略图 / 画布刷新）。留在 <see cref="MainWindowViewModel" />
///         里会让它重新长成 GodClass，而这部分逻辑与"窗口状态"其实无关。
///     </para>
///     <para>
///         <b>不持有 VM</b>：<see cref="IsDirty" /> / <see cref="CanUndo" /> / <see cref="CanRedo" />
///         用属性通知对外广播（属性名与 VM 的同名转发属性一致，VM 直接转发通知即可）；
///         撤销 / 重做需要改选中时，把"该选谁"作为返回值交回 VM——选区是控件双向绑定的本体，只能由 VM 写。
///     </para>
/// </summary>
internal sealed class FrameEditPresenter : ViewModelBase
{
    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    private readonly DocumentSession _session;
    private readonly FileWorkspace _workspace;
    private readonly FrameEditOperations _ops;
    private readonly FrameExportService _export;
    private readonly FrameCardPresenter _cards;
    private readonly ObservableCollection<FrameViewModel> _frames;
    private readonly FrameChangeTracker _changes;
    private ImgEditHistory? _history;
    private bool _isDirty;

    /// <summary>构造。</summary>
    /// <param name="session">文档会话。</param>
    /// <param name="workspace">文件工作区。</param>
    /// <param name="cards">卡片同步器（多选取自它的 <c>Selection</c>）。</param>
    /// <param name="frames">帧卡片列表。</param>
    /// <param name="changes">
    ///     变更基线。<b>由通道持有</b>并注入：基线的生命周期与本通道的 IMG 绑定，
    ///     放在这里会让"每通道一份基线"变成隐含约定。
    /// </param>
    public FrameEditPresenter(
        DocumentSession session,
        FileWorkspace workspace,
        FrameCardPresenter cards,
        ObservableCollection<FrameViewModel> frames,
        FrameChangeTracker changes)
    {
        _session = session;
        _workspace = workspace;
        _ops = new FrameEditOperations(session);
        _export = new FrameExportService();
        _cards = cards;
        _frames = frames;
        _changes = changes;
    }

    /// <summary>是否有未保存的修改（任意编辑置 <c>true</c>，保存成功由 VM 调 <see cref="MarkSaved" /> 清掉）。</summary>
    public bool IsDirty {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    /// <summary>是否可撤销。</summary>
    public bool CanUndo => _history?.CanUndo ?? false;

    /// <summary>是否可重做。</summary>
    public bool CanRedo => _history?.CanRedo ?? false;

    /// <summary>结构操作（插入 / 删除 / 移动）的编排入口。</summary>
    public FrameEditOperations Operations => _ops;

    /// <summary>
    ///     以当前 IMG 为基线重建历史栈（新 IMG 加载完 / 切换条目时调用）。
    ///     <para>旧 IMG 的快照不能再作用到新模型上，所以这里<b>换实例</b>而不是 <c>Clear()</c>。</para>
    ///     <para>同时拍一次变更基线：刚加载完的文件视为"没有任何改动"。</para>
    /// </summary>
    public void Attach() {
        _history = _session.CurrentImg is { } img ? new ImgEditHistory(img) : null;
        _changes.Capture(_session.CurrentImg);
        IsDirty = false;
        NotifyHistory();
    }

    /// <summary>丢弃历史栈与变更基线（关闭文件 / 切条目时调用）——基线随着一起清空，不会跨文档持有帧。</summary>
    public void Detach() {
        _history = null;
        _changes.Clear();
        IsDirty = false;
        NotifyHistory();
    }

    /// <summary>保存成功后清脏，并把"刚写出去的这份"作为新基线（所有变更标记归零）。</summary>
    public void MarkSaved() {
        IsDirty = false;
        _changes.Capture(_session.CurrentImg);
    }

    /// <summary>一次编辑成功后登记：压入撤销栈、清空重做栈、置脏。</summary>
    public void Record() {
        _history?.Record();
        IsDirty = true;
        NotifyHistory();
    }

    /// <summary>撤销一步。</summary>
    /// <param name="current">撤销前的当前帧（尽量保持选中它）。</param>
    /// <returns>是否生效 + 应恢复选中的帧 + 文案。</returns>
    public EditStep Undo(FrameViewModel? current) {
        if (_history is null || !_history.Undo()) {
            return new EditStep(false, null, Loc["status.undoEmpty"]);
        }

        IsDirty = true;
        NotifyHistory();
        return new EditStep(true, ResolveAnchor(current), Loc["status.undoDone"]);
    }

    /// <summary>重做一步。</summary>
    /// <param name="current">重做前的当前帧（尽量保持选中它）。</param>
    /// <returns>是否生效 + 应恢复选中的帧 + 文案。</returns>
    public EditStep Redo(FrameViewModel? current) {
        if (_history is null || !_history.Redo()) {
            return new EditStep(false, null, Loc["status.redoEmpty"]);
        }

        IsDirty = true;
        NotifyHistory();
        return new EditStep(true, ResolveAnchor(current), Loc["status.redoDone"]);
    }

    /// <summary>
    ///     裁剪选区（多选时逐帧处理）。裁剪算<b>一次</b>撤销单元；被裁过的帧会就地重建缩略图。
    /// </summary>
    /// <param name="current">当前帧（多选为空时作为唯一作用对象）。</param>
    /// <returns>裁剪结果（含被裁过的帧，供调用方决定要不要刷新画布）。</returns>
    public FrameTrimOutcome Trim(FrameViewModel? current) {
        var targets = FrameEditOperations.SelectionForEdit(_cards.Selection, current);
        var outcome = _ops.Trim(targets);
        if (!outcome.Changed) {
            return outcome;
        }

        Record();

        foreach (var frame in outcome.Trimmed) {
            var card = _frames.FirstOrDefault(item => ReferenceEquals(item.Frame, frame));
            if (card is null) continue;

            card.Thumbnail = DocumentSession.CreateThumbnail(
                DecodeQuietly(frame), DocumentSession.ThumbnailMaxWidth, DocumentSession.ThumbnailMaxHeight);
            card.RefreshMetrics();
        }

        return outcome;
    }

    /// <summary>
    ///     用一张图片替换当前帧（换图闭环）。成功后登记一次撤销单元并重建该帧缩略图。
    /// </summary>
    /// <param name="imagePath">新图路径。</param>
    /// <param name="current">当前帧。</param>
    /// <returns>替换结果（成功时带回解码后的像素，供画布刷新）。</returns>
    public async Task<ReplaceResult> ReplaceAsync(string imagePath, FrameViewModel? current) {
        if (_session.CurrentImg is null || current is null) {
            return new ReplaceResult(false, Loc["status.selectFrameFirst"], null, null);
        }

        var result = await _workspace.ReplaceFrameAsync(_session.CurrentImg, current, imagePath);
        if (result.Success && result.Image is not null && result.Frame is not null) {
            Record();
            result.Frame.Thumbnail = DocumentSession.CreateThumbnail(
                result.Image, DocumentSession.ThumbnailMaxWidth, DocumentSession.ThumbnailMaxHeight);
        }

        return result;
    }

    /// <summary>
    ///     批量导出 PNG：每帧一个文件，命名 <c>{IMG名}_{序号:D3}.png</c>（<b>原始解码像素</b>，不含画布效果）。
    /// </summary>
    /// <param name="directory">输出目录。</param>
    /// <param name="baseName">文件名前缀（通常是 IMG 名）。</param>
    /// <param name="frames">要导出的帧。</param>
    /// <returns>成功与否 + 状态栏文案。</returns>
    public OperationResult ExportPngBatch(string directory, string baseName, IReadOnlyList<FrameViewModel> frames) {
        if (_session.CurrentImg is not { } img) {
            return new OperationResult(false, Loc["status.noImage"]);
        }

        return _export.ExportPngBatch(img, frames, directory, baseName);
    }

    /// <summary>把帧序列编码成动画 GIF（解码 + LZW 在后台线程，帧数多也不卡 UI）。</summary>
    /// <param name="path">输出路径。</param>
    /// <param name="frames">要编码的帧。</param>
    /// <returns>成功与否 + 状态栏文案。</returns>
    public Task<OperationResult> ExportGifAsync(string path, IReadOnlyList<FrameViewModel> frames) {
        if (_session.CurrentImg is not { } img) {
            return Task.FromResult(new OperationResult(false, Loc["status.noImage"]));
        }

        return _export.ExportGifAsync(img, frames, path);
    }

    private void NotifyHistory() {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>撤销 / 重做后应该选中谁：原帧还在就选它，否则回落到首帧。</summary>
    private ImgFrame? ResolveAnchor(FrameViewModel? current) {
        var img = _session.CurrentImg;
        var anchor = current?.Frame;
        var restored = anchor is null ? null : img?.Frames.FirstOrDefault(frame => ReferenceEquals(frame, anchor));
        return restored ?? img?.Frames.FirstOrDefault();
    }

    /// <summary>解码一帧；失败时返回 <c>null</c>（缩略图保持为空，不影响其它帧）。</summary>
    private RgbaImage? DecodeQuietly(ImgFrame frame) {
        try {
            return _session.CurrentImg?.Decode(frame);
        } catch {
            return null;
        }
    }
}
