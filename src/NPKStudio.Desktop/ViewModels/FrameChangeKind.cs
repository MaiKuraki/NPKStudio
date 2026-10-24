namespace NPKStudio.Desktop.ViewModels;

/// <summary>
///     一帧相对"基线快照"的变更集合（**纯提示状态**：不写文件、不进撤销栈）。
///     <para>
///         <b>为什么用 Flags</b>：一帧可以同时"是插入的"又"被替换过素材"，
///         单一枚举值表达不了；撤销 / 重做又会整体回滚，所以标记不能手工累加，
///         必须每次拿当前帧表与基线逐项比对重算（见 <see cref="FrameChangeTracker" />）。
///     </para>
/// </summary>
[Flags]
internal enum FrameChangeKind {
    /// <summary>与基线一致。</summary>
    None = 0,

    /// <summary>帧数据（<see cref="NPKStudio.Core.Formats.ImgFrame.RawData" />）变了：替换素材（改尺寸也会连带置位）。</summary>
    Pixels = 1,

    /// <summary>基线里没有这一帧：新插入的。</summary>
    Inserted = 2,

    /// <summary>在帧表里的位置变了（相对顺序被拖动 / 上移下移改变）。</summary>
    Moved = 4,

    /// <summary>X / Y / 宽 / 高变了：改尺寸、裁剪透明边、调坐标。</summary>
    Geometry = 8
}
