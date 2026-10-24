using NPKStudio.Desktop.Services;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     <see cref="ResourceManifest" /> 的路径推断与查询归一化。
///     <para>
///         推断是"没有清单时"的默认路径来源，查询归一化决定文件夹叫
///         <c>sprite_xxx</c> 还是 <c>xxx</c> 都能命中同一条清单记录——两者都是本次改造的核心。
///     </para>
/// </summary>
public sealed class ResourceManifestTests
{
    /// <summary>标准 DNF 命名：<c>_</c> 即目录分隔符，且已带 <c>sprite</c> 前缀。</summary>
    [Fact]
    public void InferPath_StandardSpriteName_ExpandsUnderscores()
    {
        Assert.Equal(
            "sprite/character/priest/effect/a.img",
            ResourceManifest.InferPath("sprite_character_priest_effect", "a.img"));
    }

    /// <summary>无前缀的自起名：自动补 <c>sprite/</c>。</summary>
    [Fact]
    public void InferPath_NameWithoutSpritePrefix_PrefixesSprite()
    {
        Assert.Equal("sprite/MyMod/a.img", ResourceManifest.InferPath("MyMod", "a.img"));
    }

    /// <summary>带 <c>.NPK</c> 后缀（大写）也要先剥后缀，不能把 <c>_NPK</c> 当目录。</summary>
    [Fact]
    public void InferPath_WithUpperCaseNpkSuffix_StripsExtension()
    {
        Assert.Equal(
            "sprite/character/swordman/effect/b.img",
            ResourceManifest.InferPath("sprite_character_swordman_effect.NPK", "b.img"));
    }

    /// <summary>推断结果与 <see cref="ResourceManifest.NpkNameOf" /> 必须互为逆运算（否则分组会跑偏）。</summary>
    [Fact]
    public void InferPath_RoundTripsWithNpkNameOf()
    {
        const string stem = "sprite_character_priest_effect";
        var path = ResourceManifest.InferPath(stem, "a.img");

        Assert.Equal(stem + ".NPK", ResourceManifest.NpkNameOf(path));
    }

    /// <summary>
    ///     文件夹叫 <c>sprite_character_priest_effect</c>（<see cref="ResourceManifest.NpkNameOf" /> 产出的形态）
    ///     与叫 <c>character_priest_effect</c>（清单存 key 的形态）都要能命中。
    /// </summary>
    [Fact]
    public void TryResolve_MatchesWithAndWithoutSpritePrefix()
    {
        var manifest = WriteManifest("sprite/character/priest/effect/a.img");

        Assert.True(manifest.TryResolve("character_priest_effect", "a.img", out var direct));
        Assert.Equal("sprite/character/priest/effect/a.img", direct);

        Assert.True(manifest.TryResolve("sprite_character_priest_effect", "a.img", out var prefixed));
        Assert.Equal("sprite/character/priest/effect/a.img", prefixed);
    }

    /// <summary>文件名大小写不敏感（Windows 上就是同一个文件）。</summary>
    [Fact]
    public void TryResolve_IsCaseInsensitiveOnFileName()
    {
        var manifest = WriteManifest("sprite/character/priest/effect/a.img");

        Assert.True(manifest.TryResolve("sprite_character_priest_effect", "A.IMG", out var hit));
        Assert.Equal("sprite/character/priest/effect/a.img", hit);
    }

    /// <summary>清单里没有的文件夹不能假命中——否则会把路径猜错还当成权威。</summary>
    [Fact]
    public void TryResolve_UnknownFolder_ReturnsFalse()
    {
        var manifest = WriteManifest("sprite/character/priest/effect/a.img");

        Assert.False(manifest.TryResolve("sprite_character_atpriest_effect", "a.img", out var miss));
        Assert.Equal(string.Empty, miss);
    }

    /// <summary>一级：(文件夹名, 文件名) 精确命中。</summary>
    [Fact]
    public void Resolve_ExactHit_ReportsManifestOrigin()
    {
        var manifest = WriteManifest("sprite/character/priest/effect/a.img");

        Assert.Equal(
            PathOrigin.Manifest,
            manifest.Resolve("character_priest_effect", "a.img", "whatever", out var path));
        Assert.Equal("sprite/character/priest/effect/a.img", path);
    }

    /// <summary>二级：文件夹名对不上，但文件名在清单里唯一 → 按文件名匹配。</summary>
    [Fact]
    public void Resolve_UniqueFileName_ReportsByFileNameOrigin()
    {
        var manifest = WriteManifest("sprite/character/common/aura/18_aradrestaunt/spoon.img");

        Assert.Equal(
            PathOrigin.ByFileName,
            manifest.Resolve("a_totally_different_folder", "spoon.img", "x", out var path));
        Assert.Equal("sprite/character/common/aura/18_aradrestaunt/spoon.img", path);
    }

    /// <summary>
    ///     二级的目录消歧：同一个文件名在清单里有多个候选，用"文件夹名归一化 == 候选目录归一化"挑出唯一那个。
    ///     <para>用户真实数据里就有这种文件名：<c>icon.img</c> 同时存在于
    ///         <c>sprite/2019jiansheng/item/creature</c> 与 <c>sprite/2019jiansheng/item/title</c>。</para>
    ///     <para>
    ///         这里把文件夹名写成 <c>2019jiansheng_ITEM_creature</c>（与清单 key 只差大小写）来逼它走二级：
    ///         一级是大小写敏感的精确查表，命中不了；二级靠归一化消歧仍要挑对。
    ///     </para>
    /// </summary>
    [Fact]
    public void Resolve_AmbiguousFileName_DisambiguatesByDirectory()
    {
        var manifest = WriteManifest(
            "sprite/2019jiansheng/item/creature/icon.img",
            "sprite/2019jiansheng/item/title/icon.img");

        Assert.Equal(
            PathOrigin.ByFileName,
            manifest.Resolve("2019jiansheng_ITEM_creature", "icon.img", "x", out var path));
        Assert.Equal("sprite/2019jiansheng/item/creature/icon.img", path);
    }

    /// <summary>
    ///     消歧失败（两个候选的归一化都一样，或都不匹配）时<b>不许乱猜</b>，必须退回推断。
    /// </summary>
    [Fact]
    public void Resolve_AmbiguousButUndecidable_FallsBackToInferred()
    {
        var manifest = WriteManifest(
            "sprite/2019jiansheng/item/creature/icon.img",
            "sprite/2019jiansheng/item/title/icon.img");

        Assert.Equal(
            PathOrigin.Inferred,
            manifest.Resolve("some_unrelated_folder", "icon.img", "some_unrelated_folder", out var path));
        Assert.Equal("sprite/some/unrelated/folder/icon.img", path);
    }

    /// <summary>三级：清单里连文件名都没有 → 回退推断，且必须标记成 <see cref="PathOrigin.Inferred" />。</summary>
    [Fact]
    public void Resolve_UnknownFile_ReportsInferredOrigin()
    {
        var manifest = WriteManifest("sprite/character/priest/effect/a.img");

        Assert.Equal(PathOrigin.Inferred, manifest.Resolve("mymod", "zzz.img", "mymod", out var path));
        Assert.Equal("sprite/mymod/zzz.img", path);
    }

    /// <summary>
    ///     最关键的一条：<b>有清单时推断绝不能盖过清单</b>。
    ///     <para>
    ///         用户真实数据里的反例：文件夹叫 <c>character_common_aura_18_aradrestaunt</c>，
    ///         盲推断会拆成 <c>sprite/character/common/aura/18/aradrestaunt/</c>（错），
    ///         真值是 <c>sprite/character/common/aura/18_aradrestaunt/</c>（目录段本身带下划线）。
    ///         实测纯推断在这批 1108 个 img 上只有 539 条正确（48.6%），所以这条断言是硬要求。
    ///     </para>
    /// </summary>
    [Fact]
    public void Resolve_RealWorldCounterExample_ManifestBeatsInference()
    {
        var manifest = WriteManifest("sprite/character/common/aura/18_aradrestaunt/spindishes01.img");

        Assert.Equal(
            PathOrigin.Manifest,
            manifest.Resolve(
                "character_common_aura_18_aradrestaunt", "spindishes01.img",
                "character_common_aura_18_aradrestaunt", out var path));

        Assert.Equal("sprite/character/common/aura/18_aradrestaunt/spindishes01.img", path);

        // 反过来证明：这条路径确实是盲推断会猜错的那种（否则这个用例就没有意义了）
        Assert.Equal(
            "sprite/character/common/aura/18/aradrestaunt/spindishes01.img",
            ResourceManifest.InferPath("character_common_aura_18_aradrestaunt", "spindishes01.img"));
    }

    private static ResourceManifest WriteManifest(params string[] paths)
    {
        var file = Path.Combine(Path.GetTempPath(), "npkstudio-manifest-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllLines(file, paths.Select(p => "IMG: " + p));
        try
        {
            return ResourceManifest.Load(file);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
