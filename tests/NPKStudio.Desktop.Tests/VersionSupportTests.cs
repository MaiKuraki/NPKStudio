using NPKStudio.Core.Formats;
using NPKStudio.Desktop.Services;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     版本支持等级的纯逻辑断言（不依赖界面 / 不依赖真实样本）。
///     <para>
///         v1 / v2 = 已验证（有真实样本）；v4 / v5 / v6 = 实验性（按旧实现推导、无真实样本）；
///         v7 / v8 / v9 / Other = 仅透传（未实现 handler，只能原样保存）。
///     </para>
/// </summary>
public sealed class VersionSupportTests
{
    /// <summary>1) 逐条断言版本 → 支持等级（枚举是 internal，Theory 传 int 以避开可访问性约束）。</summary>
    [Theory]
    [InlineData((int)ImgVersion.Other, (int)VersionSupportLevel.Passthrough)]
    [InlineData((int)ImgVersion.Ver1, (int)VersionSupportLevel.Verified)]
    [InlineData((int)ImgVersion.Ver2, (int)VersionSupportLevel.Verified)]
    [InlineData((int)ImgVersion.Ver4, (int)VersionSupportLevel.Experimental)]
    [InlineData((int)ImgVersion.Ver5, (int)VersionSupportLevel.Experimental)]
    [InlineData((int)ImgVersion.Ver6, (int)VersionSupportLevel.Experimental)]
    [InlineData((int)ImgVersion.Ver7, (int)VersionSupportLevel.Passthrough)]
    [InlineData((int)ImgVersion.Ver8, (int)VersionSupportLevel.Passthrough)]
    [InlineData((int)ImgVersion.Ver9, (int)VersionSupportLevel.Passthrough)]
    public void Of_MapsEachVersionToExpectedLevel(int version, int expected) =>
        Assert.Equal((VersionSupportLevel)expected, VersionSupport.Of((ImgVersion)version));

    /// <summary>2) 三个等级各有自己的徽标 key，且不是同一份文案。</summary>
    [Fact]
    public void BadgeKey_IsDistinctPerLevel() {
        var verified = VersionSupport.BadgeKey(VersionSupportLevel.Verified);
        var experimental = VersionSupport.BadgeKey(VersionSupportLevel.Experimental);
        var passthrough = VersionSupport.BadgeKey(VersionSupportLevel.Passthrough);

        Assert.Equal("version.verified", verified);
        Assert.Equal("version.experimental", experimental);
        Assert.Equal("version.passthrough", passthrough);
        Assert.Equal(3, new[] { verified, experimental, passthrough }.Distinct().Count());
    }

    /// <summary>3) 同一 IMG 只确认一次；换 IMG / 重置后重新确认。</summary>
    [Fact]
    public void ShouldConfirmExperimentalSave_OnlyOncePerImage() {
        var confirmation = new ExperimentalSaveConfirmation();

        Assert.True(confirmation.ShouldConfirmExperimentalSave("a/walk.img", ImgVersion.Ver5, hasEdits: true, canPrompt: true));

        confirmation.MarkConfirmed("a/walk.img");
        Assert.False(confirmation.ShouldConfirmExperimentalSave("a/walk.img", ImgVersion.Ver5, hasEdits: true, canPrompt: true));

        // 换一个 IMG：要重新问
        Assert.True(confirmation.ShouldConfirmExperimentalSave("a/run.img", ImgVersion.Ver5, hasEdits: true, canPrompt: true));

        // 切 IMG 时 Reset：刚确认过的那个也要再问一次
        confirmation.Reset();
        Assert.True(confirmation.ShouldConfirmExperimentalSave("a/walk.img", ImgVersion.Ver5, hasEdits: true, canPrompt: true));
    }

    /// <summary>4) 没编辑 / 宿主不能弹窗（自动化、无头测试）时，一律不问。</summary>
    [Fact]
    public void ShouldConfirmExperimentalSave_IsFalse_WhenNoEditsOrCannotPrompt() {
        var confirmation = new ExperimentalSaveConfirmation();

        Assert.False(confirmation.ShouldConfirmExperimentalSave("a.img", ImgVersion.Ver5, hasEdits: false, canPrompt: true));
        Assert.False(confirmation.ShouldConfirmExperimentalSave("a.img", ImgVersion.Ver5, hasEdits: true, canPrompt: false));
    }

    /// <summary>5) 只有实验性版本会问；已验证（v1/v2）与仅透传（v7 / Other）永远不问。</summary>
    [Theory]
    [InlineData(ImgVersion.Ver1, false)]
    [InlineData(ImgVersion.Ver2, false)]
    [InlineData(ImgVersion.Ver4, true)]
    [InlineData(ImgVersion.Ver5, true)]
    [InlineData(ImgVersion.Ver6, true)]
    [InlineData(ImgVersion.Ver7, false)]
    [InlineData(ImgVersion.Other, false)]
    public void ShouldConfirmExperimentalSave_FollowsSupportLevel(ImgVersion version, bool expected) {
        var confirmation = new ExperimentalSaveConfirmation();

        Assert.Equal(expected,
            confirmation.ShouldConfirmExperimentalSave("a.img", version, hasEdits: true, canPrompt: true));
    }
}
