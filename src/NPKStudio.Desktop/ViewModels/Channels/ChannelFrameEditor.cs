using System.Collections.ObjectModel;

using NPKStudio.Core.Formats;
using NPKStudio.Desktop.Localization;

namespace NPKStudio.Desktop.ViewModels.Channels;

/// <summary>
///     通道的<b>帧结构编辑</b>：插入 / 删除 / 移动 / 裁剪 / 换图 / 导出，以及这些操作的收尾
///     （撤销栈登记、卡片重建、选中恢复、变更标记重算）。
///     <para>
///         <b>为什么单独成类</b>：这些操作共用同一套前置条件（有 IMG、有选区、要置脏、要登记历史）
///         和同一套收尾（状态栏文案、卡片重建、选中恢复）。留在 <see cref="FrameChannel" />
///         里会把通道重新撑成 1200 行的聚合体。
///     </para>
///     <para>
///         <b>与通道的通信是直接方法调用，不是事件 / 委托</b>：需要回写状态就写
///         <see cref="FrameChannel.Status" />，需要通知就调 <see cref="FrameChannel.Notify" />。
///         两者都是 sealed 类型上的非虚成员，JIT 可去虚拟化并内联——不产生委托、不多播。
///     </para>
///     <para>
///         <b>不持有 FrameViewModel 包装</b>：卡片的 <see cref="FrameViewModel.Thumbnail" /> 就地赋值，
///         不额外包一层"编辑态对象"；每帧一个对象的既有形态保持不变。
///     </para>
/// </summary>
internal sealed class ChannelFrameEditor
{
    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    private readonly FrameChannel _owner;
    private readonly ChannelViewState _view;
    private readonly DocumentSession _session;
    private readonly FrameEditPresenter _editor;
    private readonly FrameCardPresenter _cards;
    private readonly ObservableCollection<FrameViewModel> _frames;
    private readonly FrameChangeTracker _changes;

    /// <summary>构造。</summary>
    /// <param name="owner">聚合根（回写状态栏 / 忙碌标记与通知的唯一出口）。</param>
    /// <param name="view">视图状态（结构变了要重算包围盒与当前帧像素）。</param>
    /// <param name="session">文档会话（撤销栈与帧表都在它背后的 IMG 上）。</param>
    /// <param name="editor">撤销栈 / 裁剪 / 换图 / 导出的编排层。</param>
    /// <param name="cards">卡片同步器（重建卡片、把底层帧映射成卡片）。</param>
    /// <param name="frames">帧卡片列表。</param>
    /// <param name="changes">变更基线（由通道持有，随 IMG 生命周期走）。</param>
    public ChannelFrameEditor(
        FrameChannel owner,
        ChannelViewState view,
        DocumentSession session,
        FrameEditPresenter editor,
        FrameCardPresenter cards,
        ObservableCollection<FrameViewModel> frames,
        FrameChangeTracker changes) {
        _owner = owner;
        _view = view;
        _session = session;
        _editor = editor;
        _cards = cards;
        _frames = frames;
        _changes = changes;
    }

    /// <summary>是否有未保存的修改。</summary>
    public bool IsDirty => _editor.IsDirty;

    /// <summary>是否可撤销（绑定工具栏按钮）。</summary>
    public bool CanUndo => _editor.CanUndo;

    /// <summary>是否可重做（绑定工具栏按钮）。</summary>
    public bool CanRedo => _editor.CanRedo;

    /// <summary>有变更标记的帧数（纯提示：不写文件、不进撤销栈；保存后归零）。</summary>
    public int ChangedFrameCount => _changes.ChangedCount;

    /// <summary>在当前帧之后插入一帧 1×1 全透明帧，并选中它。</summary>
    public void InsertAfterSelected() {
        _owner.Stop();
        var r = _editor.Operations.InsertAfter(_owner.SelectedFrame);
        _owner.Status = r.Status;
        if (!r.Changed) return;

        _editor.Record();
        Rebuild();
        SelectFrameCore(r.Select);
    }

    /// <summary>删除当前选区（多选时全部删除），删完落回原位置那一帧。</summary>
    public void DeleteSelected() {
        var targets = FrameEditOperations.SelectionForEdit(_owner.SelectedFrames, _owner.SelectedFrame);
        _owner.Stop();
        var r = _editor.Operations.Delete(targets);
        _owner.Status = r.Status;
        if (!r.Changed) return;

        _editor.Record();
        Rebuild();
        SelectFrameCore(r.Select);
    }

    /// <summary>
    ///     拖拽排序：把一帧放到 <paramref name="insertIndex" /> 这个"插入缝"上。
    ///     <para>走的是与上移 / 下移同一条模型路径（<see cref="ImgFile.MoveFrame" /> + 一次 <c>Record</c>）。</para>
    /// </summary>
    public void MoveTo(FrameViewModel? frame, int insertIndex) {
        if (frame is null) {
            _owner.Status = Loc["status.selectFrameFirst"];
            return;
        }

        _owner.Stop();
        var r = _editor.Operations.MoveTo(frame, insertIndex);
        if (!r.Changed) {
            _owner.Status = r.Status;
            return;
        }

        _editor.Record();
        Rebuild();
        SelectFrameCore(r.Select);

        // 文案放在选中之后：选中被隐藏的帧会写"此帧已隐藏"的提示，不能盖掉移动结果
        _owner.Status = r.Status;
    }

    /// <summary>把当前帧在 IMG 里前后挪动 <paramref name="delta" /> 位（越界不动）。</summary>
    public void MoveSelected(int delta) {
        _owner.Stop();
        var r = _editor.Operations.Move(_owner.SelectedFrame, delta);
        _owner.Status = r.Status;
        if (!r.Changed) return;

        _editor.Record();
        Rebuild();
        SelectFrameCore(r.Select);
    }

    /// <summary>裁剪选区（多选时逐帧处理）：裁剪算<b>一次</b>撤销单元。</summary>
    public void TrimSelected() {
        _owner.Stop();
        var r = _editor.Trim(_owner.SelectedFrame);
        _owner.Status = r.Status;
        if (!r.Changed) return;

        RefreshChangeMarkers();
        _view.ResetCanvasBounds(_session.CurrentImg);
        if (_owner.SelectedFrame is { } selected && r.Trimmed.Any(frame => ReferenceEquals(frame, selected.Frame))) {
            _view.UpdateCurrentImage();
        }
    }

    /// <summary>撤销一步：恢复模型 → 重建卡片 → 恢复选中 → 刷新检查器与画布。</summary>
    public void Undo() => ApplyHistoryStep(_editor.Undo(_owner.SelectedFrame));

    /// <summary>重做一步。</summary>
    public void Redo() => ApplyHistoryStep(_editor.Redo(_owner.SelectedFrame));

    /// <summary>批量导出 PNG 到指定目录（每帧一个文件，命名 <c>{IMG名}_{序号:D3}.png</c>）。</summary>
    public void ExportPngBatch(string directory, IReadOnlyList<FrameViewModel> frames) {
        var baseName = Path.GetFileNameWithoutExtension(_owner.SelectedEntry?.Name ?? "frame");
        _owner.Status = _editor.ExportPngBatch(directory, baseName, frames).StatusMessage;
    }

    /// <summary>把当前 IMG 编码成动画 GIF（多选时只导出选中帧，否则导出全部帧）。</summary>
    public async Task ExportGifAsync(string path) {
        var frames = _owner.SelectedFrames.Count > 1
            ? FrameEditOperations.SelectionForEdit(_owner.SelectedFrames, _owner.SelectedFrame)
            : _frames.ToList();

        _owner.IsBusy = true;
        try {
            _owner.Status = (await _editor.ExportGifAsync(path, frames)).StatusMessage;
        } finally {
            _owner.IsBusy = false;
        }
    }

    /// <summary>用一张图片替换当前选中的帧（换图后刷新缩略图与画布）。</summary>
    public async Task ReplaceSelectedAsync(string imagePath) {
        // 换图会在后台改写帧字段（像素 / 几何），必须先停表，否则播放 tick 会读到半成品。
        _owner.Stop();
        _owner.IsBusy = true;
        try {
            var r = await _editor.ReplaceAsync(imagePath, _owner.SelectedFrame);
            _owner.Status = r.StatusMessage;
            if (!r.Success) return;

            _view.UpdateCurrentImage();
            _owner.Notify(nameof(FrameChannel.HasFrames));
            RefreshChangeMarkers();
        } finally {
            _owner.IsBusy = false;
        }
    }

    /// <summary>
    ///     重算帧级变更标记并刷新「改 N」计数。
    ///     <para>
    ///         一切会改模型的路径（结构操作、裁剪、换图、改几何、撤销 / 重做、加载、保存）都必须走到这里：
    ///         标记的正确性完全依赖"模型变了就重算"，漏一处就会出现"改了却没标"或"撤销了还标着"。
    ///     </para>
    ///     <para><c>internal</c> 是为了让测试能驱动"绕过 UI 直接改 Core 模型"的那条路径（无头宿主没有图片解码器）。</para>
    /// </summary>
    internal void RefreshChangeMarkers() {
        _changes.Refresh(_session.CurrentImg, _frames);
        _owner.Notify(nameof(FrameChannel.ChangedFrameCount));
        _owner.UpdateFrameCounter();
    }

    /// <summary>以当前 IMG 为基线重建撤销栈（新 IMG 加载完时调用）。</summary>
    public void Attach() => _editor.Attach();

    /// <summary>丢弃撤销栈与变更基线（关闭文件 / 切条目时调用）。</summary>
    public void Detach() => _editor.Detach();

    /// <summary>保存成功后清脏，并把"刚写出去的这份"作为新基线。</summary>
    public void MarkSaved() => _editor.MarkSaved();

    /// <summary>撤销 / 重做之后的统一收尾：卡片列表、选中、检查器、画布、帧计数全部按新模型重算。</summary>
    private void ApplyHistoryStep(EditStep step) {
        if (!step.Applied) {
            _owner.Status = step.Status;
            return;
        }

        _owner.Stop();
        Rebuild();
        SelectFrameCore(step.Select);

        // 状态栏文案放在最后：选中帧可能触发"线减大面积透明"提示，不能被它盖掉撤销结果
        _owner.Status = step.Status;
    }

    /// <summary>
    ///     按 <see cref="ImgFile.Frames" /> 重建卡片列表（复用已有卡片对象），并重算画布包围盒与帧计数。
    /// </summary>
    private void Rebuild() {
        var img = _session.CurrentImg;
        _cards.Rebuild(img);
        _view.ResetCanvasBounds(img);
        RefreshChangeMarkers();
    }

    /// <summary>把某个底层帧设为当前帧；帧已不在列表里则清空选中。</summary>
    private void SelectFrameCore(ImgFrame? frame) => _owner.SelectedFrame = _cards.Select(frame);
}
