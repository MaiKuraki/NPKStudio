namespace NPKStudio.Desktop.Services;

/// <summary>
///     缺失资源清单（<c>missing_img_details.txt</c>）的索引。
///     <para>
///         该文件按 <c>IMG: sprite/xxx/yyy.img</c> 逐行记录缺失资源的完整路径。
///         由于 NPK 文件名与内部路径**没有可靠对应规则**（例如
///         <c>sprite_character_atpriest_effect.npk</c> 里装的是 <c>sprite/character/priest/effect/...</c>），
///         所以"要精确路径"时只能以它为准。
///     </para>
///     <para>
///         但它<b>不再是强制项</b>：没有清单时用 <see cref="InferPath" /> 按输出文件名推断
///         （名字符合 DNF 命名规则 → 包内路径就对）。清单本身可以用
///         <see cref="ResourceManifestWriter" /> 从客户端已有的 NPK 目录反向生成。
///     </para>
/// </summary>
/// <summary>一条包内路径的<b>来源</b>，决定它有多可信——导出结果必须如实告诉用户。</summary>
public enum PathOrigin {
    /// <summary>清单里 (文件夹名, 文件名) 精确命中。最可信。</summary>
    Manifest,

    /// <summary>清单里没记这个文件夹名，但按文件名（必要时用目录消歧）找到了唯一候选。</summary>
    ByFileName,

    /// <summary>清单里完全没有，只能按文件夹名推断。<b>实测只 48.6% 正确</b>，必须警告。</summary>
    Inferred
}

public sealed class ResourceManifest {
    private const string PathPrefix = "sprite/";

    private readonly Dictionary<(string Folder, string File), string> _map = new();

    /// <summary>文件名 → 清单里出现过该文件名的所有完整路径（用于 <see cref="PathOrigin.ByFileName" />）。</summary>
    private readonly Dictionary<string, List<string>> _byFileName = new(StringComparer.Ordinal);

    private ResourceManifest() { }

    /// <summary>清单中记录的路径条数。</summary>
    public int Count => _map.Count;

    /// <summary>解析清单文件。</summary>
    public static ResourceManifest Load(string manifestPath) {
        var manifest = new ResourceManifest();
        foreach (var raw in File.ReadLines(manifestPath)) {
            var line = raw.Trim();
            if (!line.StartsWith("IMG: ", StringComparison.Ordinal)) continue;

            var fullPath = line[5..].Trim();
            if (fullPath.Length == 0 || !fullPath.EndsWith(".img", StringComparison.OrdinalIgnoreCase)) continue;

            var slash = fullPath.LastIndexOf('/');
            if (slash <= 0) continue;

            var directory = fullPath[..slash];
            var fileName = fullPath[(slash + 1)..];

            var folder = directory.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase)
                ? directory[PathPrefix.Length..]
                : directory;
            folder = folder.Replace('/', '_');

            var file = fileName.ToLowerInvariant();
            manifest._map[(folder, file)] = fullPath;

            if (!manifest._byFileName.TryGetValue(file, out var candidates)) {
                manifest._byFileName[file] = candidates = [];
            }

            // 同一路径在清单里可能重复出现（同一资源被多处引用），只记一次，避免"歧义"误判
            if (!candidates.Contains(fullPath, StringComparer.Ordinal)) {
                candidates.Add(fullPath);
            }
        }

        return manifest;
    }

    /// <summary>
    ///     由"导出文件夹名 + 文件名"查回 NPK 内的完整条目路径。
    ///     <para>
    ///         查询侧会做与 <see cref="Load" /> 对称的归一化：先按原名查，再剥掉开头的
    ///         <c>sprite_</c> 查一次。这样导出文件夹叫 <c>sprite_character_priest_effect</c>
    ///         或叫 <c>character_priest_effect</c> 都能命中同一条清单记录——
    ///         <see cref="NpkNameOf" /> 产出的目录名带 <c>sprite_</c> 前缀，而用户手建的目录常常不带。
    ///     </para>
    /// </summary>
    /// <param name="folderName">导出文件夹名（可带或不带 <c>sprite_</c> 前缀）。</param>
    /// <param name="fileName">文件名（大小写不敏感）。</param>
    /// <param name="fullPath">命中时的完整包内路径；未命中时为 <see cref="string.Empty" />。</param>
    /// <returns>是否命中。</returns>
    public bool TryResolve(string folderName, string fileName, out string fullPath) {
        if (string.IsNullOrEmpty(folderName)) {
            fullPath = string.Empty;
            return false;
        }

        var file = (fileName ?? string.Empty).ToLowerInvariant();
        if (_map.TryGetValue((folderName, file), out var hit)) {
            fullPath = hit;
            return true;
        }

        var stripped = StripSpritePrefix(folderName);
        if (!string.Equals(stripped, folderName, StringComparison.Ordinal) &&
            _map.TryGetValue((stripped, file), out hit)) {
            fullPath = hit;
            return true;
        }

        fullPath = string.Empty;
        return false;
    }

    /// <summary>
    ///     三级解析：① (文件夹名,文件名) 精确 → ② 文件名唯一 / 目录消歧 → ③ 按输出名推断。
    ///     <para>
    ///         分级的依据是实测（用户真实数据 <c>NPK_ForBuHui</c> 1108 个 img）：
    ///         <list type="bullet">
    ///             <item>① 命中 1108/1108（100%）；</item>
    ///             <item>② 文件名全局唯一可命中 873/1108（78.8%），加上目录消歧还能多救回一部分；</item>
    ///             <item>③ 盲推断只有 539/1108（48.6%）正确——因为 DNF 的目录段<b>本身带下划线</b>，
    ///                 例如 <c>character_common_aura_18_aradrestaunt</c> 的真值目录是
    ///                 <c>sprite/character/common/aura/<b>18_aradrestaunt</b></c>，
    ///                 盲推会把它拆成 <c>.../aura/18/aradrestaunt</c>。</item>
    ///         </list>
    ///         所以③只能兜底，且必须把条数如实报给用户。
    ///     </para>
    /// </summary>
    /// <param name="folderName">导出文件夹名。</param>
    /// <param name="fileName">条目文件名。</param>
    /// <param name="npkStem">输出 NPK 名主干（仅③用到）。</param>
    /// <param name="fullPath">解析出的完整包内路径。</param>
    /// <returns>这条路径的来源。</returns>
    public PathOrigin Resolve(string folderName, string fileName, string npkStem, out string fullPath) {
        if (TryResolve(folderName, fileName, out fullPath)) {
            return PathOrigin.Manifest;
        }

        var file = (fileName ?? string.Empty).ToLowerInvariant();
        if (_byFileName.TryGetValue(file, out var candidates)) {
            if (candidates.Count == 1) {
                fullPath = candidates[0];
                return PathOrigin.ByFileName;
            }

            // 歧义消解：候选目录与导出文件夹名都归一化（剥 sprite/ → 去掉 / 和 _）后比较。
            // 例：文件夹 2019jiansheng_item_creature 的 icon.img 有两个候选，
            //     sprite/2019jiansheng/item/{creature,title}/icon.img → 归一化后只有 creature 那个匹配。
            var wanted = NormalizeDirectory(folderName);
            var hit = default(string);
            foreach (var candidate in candidates) {
                var slash = candidate.LastIndexOf('/');
                if (slash <= 0) continue;
                if (!string.Equals(NormalizeDirectory(candidate[..slash]), wanted, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                if (hit is not null) { hit = null; break; } // 消歧后仍不唯一 → 放弃，交给推断
                hit = candidate;
            }

            if (hit is not null) {
                fullPath = hit;
                return PathOrigin.ByFileName;
            }
        }

        fullPath = InferPath(npkStem, fileName ?? string.Empty);
        return PathOrigin.Inferred;
    }

    /// <summary>目录名归一化：剥掉 <c>sprite/</c> 前缀，再去掉所有 <c>/</c> 与 <c>_</c>。</summary>
    private static string NormalizeDirectory(string directory) {
        var text = directory.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase)
            ? directory[PathPrefix.Length..]
            : directory;

        return text.Replace("/", string.Empty, StringComparison.Ordinal)
                   .Replace("_", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    ///     <b>无清单时</b>的路径推断：NPK 文件名（去扩展名）里的 <c>_</c> 就是包内目录分隔符。
    ///     <para>
    ///         例：<c>sprite_character_priest_effect</c> + <c>a.img</c> →
    ///         <c>sprite/character/priest/effect/a.img</c>；<c>MyMod</c> + <c>a.img</c> →
    ///         <c>sprite/MyMod/a.img</c>。
    ///     </para>
    ///     <para>
    ///         这条规则<b>不是总成立</b>（<c>sprite_character_atpriest_effect.npk</c> 装的是
    ///         <c>sprite/character/priest/effect/...</c>），所以它只在没有清单时兜底；
    ///         有清单时一律以清单为准。用户只要把输出文件名起成正确的 NPK 名，推断结果就是对的。
    ///     </para>
    /// </summary>
    /// <param name="npkStem">NPK 文件名主干（可带 <c>.npk</c> 扩展名）。</param>
    /// <param name="fileName">条目文件名，如 <c>a.img</c>。</param>
    /// <returns>推断出的包内完整路径。</returns>
    public static string InferPath(string npkStem, string fileName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var stem = (npkStem ?? string.Empty).Trim();
        if (stem.EndsWith(".npk", StringComparison.OrdinalIgnoreCase)) {
            stem = stem[..^".npk".Length];
        }

        var directory = stem.Replace('_', '/').Trim('/');
        if (directory.Length == 0) {
            directory = "sprite";
        } else if (!directory.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase)) {
            directory = PathPrefix + directory;
        }

        return directory + "/" + fileName;
    }

    /// <summary>剥掉文件夹名开头的 <c>sprite_</c>（与 <see cref="Load" /> 存 key 时的口径对称）。</summary>
    private static string StripSpritePrefix(string folderName) =>
        folderName.StartsWith("sprite_", StringComparison.OrdinalIgnoreCase)
            ? folderName["sprite_".Length..]
            : folderName;

    /// <summary>由完整条目路径推出它所属的 NPK 文件名，例如 <c>sprite/a/b/c.img</c> → <c>sprite_a_b.NPK</c>。</summary>
    public static string NpkNameOf(string fullPath) {
        var slash = fullPath.LastIndexOf('/');
        var directory = slash > 0 ? fullPath[..slash] : string.Empty;
        var name = directory.Replace('/', '_');
        if (!name.StartsWith("sprite_", StringComparison.OrdinalIgnoreCase)) {
            name = "sprite_" + name;
        }

        return name + ".NPK";
    }
}
