using NPKStudio.Core.Formats;
using NPKStudio.Desktop.Localization;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>一次帧结构编辑的结果：是否真的改了模型、状态栏文案、以及改完之后该选中哪一帧。</summary>
/// <param name="Changed">模型是否发生变化（<c>false</c> 时调用方不要刷新列表）。</param>
/// <param name="Status">已经格式化好的状态栏文案。</param>
/// <param name="Select">编辑后应选中的帧；<c>null</c> 表示列表已空。</param>
internal readonly record struct FrameEditOutcome(bool Changed, string Status, ImgFrame? Select);

/// <summary>一次裁剪的结果：是否真的裁了、状态栏文案、被裁掉的帧（调用方据此重建缩略图）。</summary>
/// <param name="Changed">是否至少有一帧真的被裁（<c>false</c> 时不要动缩略图）。</param>
/// <param name="Status">已经格式化好的状态栏文案。</param>
/// <param name="Trimmed">真的被裁过的帧（像素尺寸已变，缩略图必须重建）。</param>
internal readonly record struct FrameTrimOutcome(bool Changed, string Status, IReadOnlyList<ImgFrame> Trimmed);

/// <summary>
///     帧结构编辑的编排层：插入 / 删除 / 移动。
///     <para>
///         <b>为什么单独成类</b>：这三件事都要经过"挑作用范围 → 调 Core → 决定删完选中谁 → 出状态文案"，
///         放进 <see cref="MainWindowViewModel" /> 会让它继续膨胀。这里只碰 <see cref="DocumentSession" />，
///         <b>不持有 VM、不碰 UI 集合</b>；列表重建与选中同步由 VM 自己做（那是视图状态，不属于文档）。
///     </para>
/// </summary>
internal sealed class FrameEditOperations
{
    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    private readonly DocumentSession _session;

    public FrameEditOperations(DocumentSession session) => _session = session;

    /// <summary>在当前帧之后插入一帧 1×1 全透明帧。</summary>
    /// <param name="current">当前选中帧；<c>null</c> 时追加到末尾。</param>
    /// <returns>编辑结果。</returns>
    public FrameEditOutcome InsertAfter(FrameViewModel? current)
    {
        if (_session.CurrentImg is not { } img)
        {
            return new FrameEditOutcome(false, Loc["status.noImage"], null);
        }

        var index = current is null ? img.Frames.Count : current.Frame.Index + 1;

        ImgFrame inserted;
        try
        {
            inserted = img.InsertFrame(index);
        }
        catch (Exception ex)
        {
            return new FrameEditOutcome(false, Describe(img, ex), null);
        }

        return new FrameEditOutcome(true, Loc.F("status.frameInserted", $"#{inserted.Index:D3}"), inserted);
    }

    /// <summary>删除一组帧；删完落回原位置那一帧。</summary>
    /// <param name="targets">作用范围（多选时按 Index 升序，由调用方保证）。</param>
    /// <returns>编辑结果。</returns>
    public FrameEditOutcome Delete(IReadOnlyList<FrameViewModel> targets)
    {
        if (_session.CurrentImg is not { } img)
        {
            return new FrameEditOutcome(false, Loc["status.noImage"], null);
        }

        if (targets.Count == 0)
        {
            return new FrameEditOutcome(false, Loc["status.selectFrameFirst"], null);
        }

        // 删除前记下"原位置"：删完它还在就选它，否则选新的末帧
        var anchor = targets[0].Frame.Index;

        var removed = 0;
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                if (img.RemoveFrame(targets[i].Frame))
                {
                    removed++;
                }
            }
        }
        catch (Exception ex)
        {
            return new FrameEditOutcome(removed > 0, Describe(img, ex), null);
        }

        if (removed == 0)
        {
            return new FrameEditOutcome(false, Loc["status.selectFrameFirst"], null);
        }

        var next = img.Frames.Count == 0
            ? null
            : img.Frames[Math.Clamp(anchor, 0, img.Frames.Count - 1)];

        return new FrameEditOutcome(true, Loc.F("status.framesDeleted", removed), next);
    }

    /// <summary>把当前帧在 IMG 里前后挪动 <paramref name="delta" /> 位（越界不动）。</summary>
    /// <param name="current">当前选中帧。</param>
    /// <param name="delta">位移（负数为上移）。</param>
    /// <returns>编辑结果。</returns>
    public FrameEditOutcome Move(FrameViewModel? current, int delta)
    {
        if (_session.CurrentImg is not { } img || current is null)
        {
            return new FrameEditOutcome(false, Loc["status.selectFrameFirst"], null);
        }

        var from = current.Frame.Index;
        var to = from + delta;
        if (to < 0 || to >= img.Frames.Count)
        {
            return new FrameEditOutcome(false, Loc["status.frameMoveEdge"], current.Frame);
        }

        try
        {
            if (!img.MoveFrame(from, to))
            {
                return new FrameEditOutcome(false, Loc["status.frameMoveEdge"], current.Frame);
            }
        }
        catch (Exception ex)
        {
            return new FrameEditOutcome(false, Describe(img, ex), current.Frame);
        }

        return new FrameEditOutcome(true, Loc.F("status.frameMoved", $"#{from:D3}", $"#{to:D3}"), current.Frame);
    }

    /// <summary>
    ///     把当前帧移动到 <paramref name="insertIndex" /> 这个"插入缝"上（拖拽排序用）。
    ///     <para>
    ///         <paramref name="insertIndex" /> 是<b>原始列表</b>里的插入位置（<c>0..Count</c>，
    ///         含义是"插到第 insertIndex 个元素之前"）；内部换算成 <see cref="ImgFile.MoveFrame" />
    ///         需要的目标下标（减一），因此语义与上移 / 下移完全一致，撤销栈也只有一步。
    ///     </para>
    /// </summary>
    /// <param name="current">被拖动的帧。</param>
    /// <param name="insertIndex">插入缝下标（<c>0..帧数</c>，越界会被夹住）。</param>
    /// <returns>编辑结果（顺序没变时 <see cref="FrameEditOutcome.Changed" /> 为 <c>false</c>）。</returns>
    public FrameEditOutcome MoveTo(FrameViewModel? current, int insertIndex)
    {
        if (_session.CurrentImg is not { } img || current is null)
        {
            return new FrameEditOutcome(false, Loc["status.selectFrameFirst"], null);
        }

        var from = current.Frame.Index;
        var clamped = Math.Clamp(insertIndex, 0, img.Frames.Count);
        // 先摘后插：插在"自己后面"的缝上等于原地不动，所以统一用 from 之后的位置减一
        var to = clamped > from ? clamped - 1 : clamped;
        if (to == from)
        {
            return new FrameEditOutcome(false, Loc["status.frameMoveNoop"], current.Frame);
        }

        if (to < 0 || to >= img.Frames.Count)
        {
            return new FrameEditOutcome(false, Loc["status.frameMoveEdge"], current.Frame);
        }

        try
        {
            if (!img.MoveFrame(from, to))
            {
                return new FrameEditOutcome(false, Loc["status.frameMoveEdge"], current.Frame);
            }
        }
        catch (Exception ex)
        {
            return new FrameEditOutcome(false, Describe(img, ex), current.Frame);
        }

        return new FrameEditOutcome(
            true,
            Loc.F("status.frameMoved", $"#{from:D3}", $"#{to:D3}"),
            current.Frame);
    }

    /// <summary>
    ///     裁掉一组帧四周的全透明边（多选时逐帧处理）。
    ///     <para>
    ///         LINK 帧没有自己的像素，直接跳过；解码失败的帧计入失败数但<b>不中断</b>其余帧。
    ///         全部返回 <c>false</c>（已是紧包围盒）时给出"无需裁剪"的提示，不算一次编辑。
    ///     </para>
    /// </summary>
    /// <param name="targets">作用范围（多选时为全部选中帧）。</param>
    /// <returns>裁剪结果：是否真的改了模型、状态文案、被裁过的帧。</returns>
    public FrameTrimOutcome Trim(IReadOnlyList<FrameViewModel> targets)
    {
        if (_session.CurrentImg is not { } img)
        {
            return new FrameTrimOutcome(false, Loc["status.noImage"], []);
        }

        if (targets.Count == 0)
        {
            return new FrameTrimOutcome(false, Loc["status.selectFrameFirst"], []);
        }

        var trimmed = new List<ImgFrame>(targets.Count);
        var failed = 0;

        foreach (var target in targets)
        {
            var frame = target.Frame;
            if (frame.IsLink)
            {
                continue;
            }

            try
            {
                if (img.TrimFrame(frame))
                {
                    trimmed.Add(frame);
                }
            }
            catch (Exception)
            {
                failed++;
            }
        }

        if (trimmed.Count == 0)
        {
            return failed > 0
                ? new FrameTrimOutcome(false, Loc.F("status.trimFailed", failed), [])
                : new FrameTrimOutcome(false, Loc["status.trimNoop"], []);
        }

        return new FrameTrimOutcome(true, Loc.F("status.trimmed", trimmed.Count), trimmed);
    }

    /// <summary>
    ///     本次结构操作作用在哪些帧上：多选取全部（按 Index 升序，删除时才不会错位），否则只取当前帧。
    /// </summary>
    /// <param name="selectedFrames">多选集合。</param>
    /// <param name="current">当前帧。</param>
    /// <returns>按 Index 升序的作用范围。</returns>
    public static List<FrameViewModel> SelectionForEdit(
        IReadOnlyCollection<FrameViewModel> selectedFrames, FrameViewModel? current)
    {
        if (selectedFrames.Count > 1)
        {
            var list = new List<FrameViewModel>(selectedFrames.Count);
            list.AddRange(selectedFrames);
            list.Sort(static (a, b) => a.Frame.Index.CompareTo(b.Frame.Index));
            return list;
        }

        var single = current ?? selectedFrames.FirstOrDefault();
        return single is null ? [] : [single];
    }

    /// <summary>把 Core 的异常收敛成一句可读文案（Core 是纯模型层，不该知道界面文案）。</summary>
    /// <param name="img">当前 IMG（用于取版本号）。</param>
    /// <param name="ex">原始异常。</param>
    /// <returns>状态栏文案。</returns>
    private static string Describe(ImgFile img, Exception ex) => ex is NotSupportedException
        ? Loc.F("status.frameEditUnsupported", (int)img.Version)
        : Loc.F("status.frameEditFailed", ex.Message);
}
