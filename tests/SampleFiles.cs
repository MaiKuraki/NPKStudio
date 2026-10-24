using System.IO;

namespace NPKStudio.TestSupport;

/// <summary>
///     测试样本的定位与可用性探测（两个测试工程 + 基准工程共享的链接文件）。
///     <para>
///         <b>为什么需要它</b>：真实客户端 NPK 是第三方版权资产，不能随仓库分发。
///         仓库里只带一份程序化生成的 <c>synthetic_shapes.NPK</c>；真实样本由开发者自备
///         （放进 <c>tests/samples/</c>，已被 .gitignore 忽略）。因此"样本可能在也可能不在"，
///         测试必须先探测再决定走哪条路，而不是假设文件一定存在。
///     </para>
///     <para>
///         路径一律从 <see cref="AppContext.BaseDirectory" /> 出发：三个工程都在 csproj 里把
///         <c>..\samples\*.NPK</c> 拷到输出目录的 <c>samples/</c> 下，所以运行时位置固定。
///     </para>
/// </summary>
internal static class SampleFiles
{
    /// <summary>真实样本的文件名（自备；缺失时相关用例静默跳过）。</summary>
    public const string RealSampleName = "real_sample.NPK";

    /// <summary>合成样本的文件名（随仓库提交）。</summary>
    public const string SyntheticSampleName = "synthetic_shapes.NPK";

    /// <summary>真实样本的完整路径（不保证存在）。</summary>
    public static string RealSamplePath { get; } = Resolve(RealSampleName);

    /// <summary>真实样本是否存在。为 <c>false</c> 时依赖它的用例必须原样返回。</summary>
    public static bool RealSampleAvailable { get; } = File.Exists(RealSamplePath);

    /// <summary>合成样本的完整路径（不保证存在，但正常构建下一定存在）。</summary>
    public static string SyntheticSamplePath { get; } = Resolve(SyntheticSampleName);

    /// <summary>合成样本是否存在。</summary>
    public static bool SyntheticSampleAvailable { get; } = File.Exists(SyntheticSamplePath);

    private static string Resolve(string name) => Path.Combine(AppContext.BaseDirectory, "samples", name);
}
