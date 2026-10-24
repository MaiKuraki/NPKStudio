using System.Collections.ObjectModel;

using NPKStudio.Core.Formats;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>
///     右栏帧卡片列表的同步器：把 <see cref="ImgFile.Frames" /> 映射成
///     <see cref="ObservableCollection{T}" /> 里的卡片，并维护"当前选中 / 多选 / 检查器"三者的联动。
///     <para>
///         <b>为什么单独成类</b>：这段逻辑只关心"帧表 → 卡片 → 检查器"的投影，不关心文档、
///         导出或播放；留在 <see cref="MainWindowViewModel" /> 里会让它继续长成一个 GodClass。
///     </para>
///     <para>
///         <b>只注入对象，不注入委托</b>：三个集合 / 检查器直接按引用注入。
///         唯一的"当前选中"以方法参数传入——它是 Avalonia <c>ListBox</c> 双向绑定的本体，
///         必须由 VM 持有，卡片层只读不写。
///     </para>
/// </summary>
internal sealed class FrameCardPresenter
{
    private readonly ObservableCollection<FrameViewModel> _frames;
    private readonly FrameInspector _inspector;

    public FrameCardPresenter(
        ObservableCollection<FrameViewModel> frames,
        ObservableCollection<FrameViewModel> selectedFrames,
        FrameInspector inspector)
    {
        _frames = frames;
        _inspector = inspector;
        Selection = selectedFrames;
    }

    /// <summary>
    ///     多选集合（与 <c>ListBox.SelectedItems</c> 共用同一个实例）。
    ///     <para>
    ///         <see cref="MainWindowViewModel.SelectedFrames" /> 的 setter 会换实例（绑定初始化时控件可能把自己的集合推上来），
    ///         所以这里必须跟着换，不能一直抓着构造时的那一份。
    ///     </para>
    /// </summary>
    public ObservableCollection<FrameViewModel> Selection { get; set; }

    /// <summary>
    ///     按 <see cref="ImgFile.Frames" /> 重建卡片列表，<b>按引用复用已有卡片对象</b>——
    ///     否则每次增删都要重新解码全部缩略图。
    /// </summary>
    /// <param name="image">当前 IMG；<c>null</c> 时清空列表。</param>
    public void Rebuild(ImgFile? image)
    {
        var cache = new Dictionary<ImgFrame, FrameViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var item in _frames)
        {
            cache[item.Frame] = item;
        }

        var rebuilt = new List<FrameViewModel>(image?.Frames.Count ?? 0);
        if (image is not null)
        {
            foreach (var frame in image.Frames)
            {
                rebuilt.Add(cache.TryGetValue(frame, out var existing) ? existing : new FrameViewModel(frame));
            }
        }

        _frames.Clear();
        foreach (var item in rebuilt)
        {
            _frames.Add(item);
        }

        // 序号会重排（增删 / 移动后 Index 变了），复用对象必须显式刷新派生文案
        foreach (var item in rebuilt)
        {
            item.RefreshMetrics();
        }
    }

    /// <summary>
    ///     把某个底层帧设为当前帧（清空多选、只选中它），返回应赋给 VM <c>SelectedFrame</c> 的卡片。
    ///     <para>帧已不在列表里时清空选中，避免"列表里没有、右边还显示上一张图"。</para>
    /// </summary>
    /// <param name="frame">要选中的底层帧；<c>null</c> 表示清空。</param>
    /// <returns>对应的卡片，或 <c>null</c>。</returns>
    public FrameViewModel? Select(ImgFrame? frame)
    {
        var vm = frame is null ? null : _frames.FirstOrDefault(item => ReferenceEquals(item.Frame, frame));

        // 先清掉旧的多选：控件选区与 SelectedFrames 是同一份数据，直接改它比改 SelectedItem 更可靠
        Selection.Clear();
        if (vm is not null)
        {
            Selection.Add(vm);
        }

        SyncInspector(vm);
        return vm;
    }

    /// <summary>把选区推给检查器：多选只关心帧数，位置/尺寸控件会被禁用。</summary>
    /// <param name="primary">当前帧（<c>ListBox.SelectedItem</c>）。</param>
    public void SyncInspector(FrameViewModel? primary)
    {
        var count = Selection.Count > 1 ? Selection.Count : primary is null ? 0 : 1;
        _inspector.Show(primary, count);
    }
}
