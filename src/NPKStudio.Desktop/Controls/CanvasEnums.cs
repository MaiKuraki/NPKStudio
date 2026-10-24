namespace NPKStudio.Desktop.Controls;

/// <summary>画布帧内背景的显示方式（共 7 档，全部由控件在外层绘制，不参与像素计算）。</summary>
public enum CanvasBackground {
    /// <summary>主题棋盘格（表示透明区域，默认）。</summary>
    Checker,

    /// <summary>深灰纯色。</summary>
    Dark,

    /// <summary>浅灰纯色。</summary>
    Light,

    /// <summary>绿幕色（检查抠图边缘）。</summary>
    Green,

    /// <summary>纯黑（特效贴图最中性的底色）。</summary>
    Black,

    /// <summary>亮棋盘格（#FFFFFF / #E6E9EE，浅色或中等亮度特效）。</summary>
    CheckerLight,

    /// <summary>暗棋盘格（#808080 / #6B6B6B，暗淡特效）。</summary>
    CheckerDark
}

/// <summary>
///     标尺 / 游标 / 参考线标签的坐标基准。
///     <para>
///         同一批参考线在两套基准下<b>位置完全相同</b>，只有标签与刻度上的数字不同：
///         参考线内部永远按角色坐标存储（那是写进 IMG 的那套数），基准只影响"怎么读"。
///     </para>
/// </summary>
public enum RulerOriginMode {
    /// <summary>图像坐标（默认）：0,0 = 当前 IMG 画布（联合包围盒）左上角，符合 Photoshop 等工具的惯例。</summary>
    Image,

    /// <summary>角色坐标：0,0 = 角色坐标系原点，数值与检查器里的 X/Y 一致，用于多帧对齐与 PVF 对照。</summary>
    Role
}
