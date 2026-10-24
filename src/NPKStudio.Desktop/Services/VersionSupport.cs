using NPKStudio.Core.Formats;

namespace NPKStudio.Desktop.Services;

/// <summary>
///     IMG 版本的支持等级（界面策略）。
///     <para>等级决定"读放行、写警告、不支持说清楚"三件事：能不能解析、能不能编辑、界面要不要提示风险。</para>
/// </summary>
internal enum VersionSupportLevel {
    /// <summary>有真实样本验证过读写，界面不做任何警示。</summary>
    Verified,

    /// <summary>能读能写，但写回逻辑按旧版实现推导、<b>尚无真实样本验证</b>，保存前要确认一次。</summary>
    Experimental,

    /// <summary>无法解析帧，只能整段原样保存；帧编辑一律禁用。</summary>
    Passthrough
}

/// <summary>
///     版本 → 支持等级的<b>唯一判定点</b>。
///     <para>
///         这是界面策略，刻意不进 <c>NPKStudio.Core</c>：格式层只回答"这个版本能不能读"，
///         "该不该警告用户"属于产品决定，放在 Desktop 才不会污染格式实现。
///     </para>
/// </summary>
internal static class VersionSupport {
    /// <summary>
    ///     判定版本的支持等级。
    ///     <para>
    ///         只有 <b>v1 / v2 有真实样本</b>（本机与用户手上全部素材都是 v2）。
    ///         v4 / v5 / v6 的读写是照旧版 ExtractorSharp 的 handler 源码推导 + 手工构造样本实现的，
    ///         从未用真实文件验证过；v5 写回还没有 DXT 编码，只会降级成 ARGB1555。
    ///         因此这三个版本一律归入 <see cref="VersionSupportLevel.Experimental" />，
    ///         <b>只有拿到真实样本逐字节验证通过后，才能改成 <see cref="VersionSupportLevel.Verified" /></b>。
    ///     </para>
    ///     <para>v7 / v8 / v9 未实现（落 <c>OtherHandler</c>），以及 <c>Other</c>（.ogg 等非 IMG）都是仅透传。</para>
    /// </summary>
    /// <param name="version">IMG 头里的版本号。</param>
    /// <returns>支持等级。</returns>
    internal static VersionSupportLevel Of(ImgVersion version) => version switch {
        ImgVersion.Ver1 or ImgVersion.Ver2 => VersionSupportLevel.Verified,
        ImgVersion.Ver4 or ImgVersion.Ver5 or ImgVersion.Ver6 => VersionSupportLevel.Experimental,
        _ => VersionSupportLevel.Passthrough
    };

    /// <summary>
    ///     等级徽标的本地化 key。
    ///     <para>已验证版本也有 key，但界面默认不显示"已验证"字样——正常态不该出现任何警示。</para>
    /// </summary>
    /// <param name="level">支持等级。</param>
    /// <returns>本地化 key。</returns>
    internal static string BadgeKey(VersionSupportLevel level) => level switch {
        VersionSupportLevel.Verified => "version.verified",
        VersionSupportLevel.Experimental => "version.experimental",
        _ => "version.passthrough"
    };
}
