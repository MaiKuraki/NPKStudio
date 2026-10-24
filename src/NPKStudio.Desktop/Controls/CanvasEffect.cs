namespace NPKStudio.Desktop.Controls;

/// <summary>
///     画布效果，对应 ani 的 <c>[GRAPHIC EFFECT]</c>。
///     <para>
///         只保留<b>有确定性证据</b>的两项：
///         <c>NONE</c>（原样）与 <c>LINEARDODGE</c>（按 pvfUtility 公开的 <c>LinearBrun</c> 行为实现，未复制其代码）。
///         其余 DODGE / DARK / XOR / MONOCHROME / SPACEDISTORT 只有社区语法注释、无法验证游戏行为，故不提供。
///     </para>
///     <para>只影响画布显示，导出 / 替换 / 另存 / 缩略图一律走原始数据。</para>
/// </summary>
public enum CanvasEffect {
    /// <summary>原图（不做任何处理）。</summary>
    None = 0,

    /// <summary>线性减淡：逐字节复刻 pvfUtility 的 <c>LinearBrun</c>。</summary>
    LinearDodge = 1
}
