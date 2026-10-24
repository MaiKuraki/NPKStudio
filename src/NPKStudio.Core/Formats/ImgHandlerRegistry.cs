using System.Collections.Concurrent;

using NPKStudio.Core.Diagnostics;
using NPKStudio.Core.Formats.Handlers;

namespace NPKStudio.Core.Formats;

/// <summary>
///     IMG 版本到处理器的注册表。
///     <para>
///         <see cref="Register" /> <b>幂等</b>（D-08）：重复注册同一版本只做"替换 + 日志"，不抛异常、不保留第二张字典。
///         未注册的版本一律回落到 <see cref="OtherHandler" />（整段取样，不解析帧）。
///     </para>
/// </summary>
public static class ImgHandlerRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ImgVersion, Func<IImgHandler>> Factories = [];

    /// <summary>
    ///     注册表的只读快照。<see cref="Get" /> 读它时<b>不取锁</b>——注册表只在静态构造/显式注册时变化，
    ///     而 <c>Get</c> 是每个 IMG 都会走的路径（旧实现每次都 <c>lock</c>）。
    /// </summary>
    private static volatile IReadOnlyDictionary<ImgVersion, Func<IImgHandler>>? _snapshot;

    /// <summary>
    ///     已经打过"版本未实现"告警的版本（避免 1108 个条目刷屏）。
    ///     <para>用 <see cref="ConcurrentDictionary{TKey,TValue}" /> 而不是 <c>HashSet + lock</c>：未命中版本的告警也不该抢锁。</para>
    /// </summary>
    private static readonly ConcurrentDictionary<ImgVersion, byte> WarnedVersions = new();

    static ImgHandlerRegistry()
    {
        Register(ImgVersion.Other, static () => new OtherHandler());
        Register(ImgVersion.Ver1, static () => new Ver1Handler());
        Register(ImgVersion.Ver2, static () => new Ver2Handler());
        Register(ImgVersion.Ver4, static () => new Ver4Handler());
        Register(ImgVersion.Ver5, static () => new Ver5Handler());
        Register(ImgVersion.Ver6, static () => new Ver6Handler());
    }

    /// <summary>
    ///     取指定版本的处理器实例（每次调用返回新实例，handler 本身无状态，可安全并发使用）。
    /// </summary>
    /// <param name="version">版本号。</param>
    /// <returns>已注册的处理器；未注册时返回 <see cref="OtherHandler" />。</returns>
    public static IImgHandler Get(ImgVersion version)
    {
        // volatile 读 + 字典查找，不取锁
        var map = _snapshot ?? BuildSnapshot();
        if (map.TryGetValue(version, out var factory))
        {
            return factory();
        }

        WarnOnce(version);
        return new OtherHandler();
    }

    /// <summary>取（必要时构建）只读快照。首次调用或注册后才会走到这里。</summary>
    private static IReadOnlyDictionary<ImgVersion, Func<IImgHandler>> BuildSnapshot()
    {
        lock (Gate)
        {
            return _snapshot ??= new Dictionary<ImgVersion, Func<IImgHandler>>(Factories);
        }
    }

    /// <summary>同一版本只打一条"未实现"告警。</summary>
    private static void WarnOnce(ImgVersion version)
    {
        if (WarnedVersions.TryAdd(version, 0))
        {
            TraceLog.Instance.Warn($"IMG 版本 {version}（0x{(int)version:X2}）尚未实现，按 Other 整段取样（同版本只提示一次）。");
        }
    }

    /// <summary>
    ///     注册（或替换）某版本的处理器工厂。幂等：重复注册只替换并记一条日志（D-08）。
    /// </summary>
    /// <param name="version">版本号。</param>
    /// <param name="factory">处理器工厂，不能为 <c>null</c>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory" /> 为 <c>null</c>。</exception>
    public static void Register(ImgVersion version, Func<IImgHandler> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (Gate)
        {
            if (Factories.ContainsKey(version))
            {
                TraceLog.Instance.Warn($"IMG 版本 {version} 的处理器被重复注册，已覆盖（幂等，不抛异常）。");
                Factories[version] = factory;
            }
            else
            {
                Factories.Add(version, factory);
            }

            // 发布新快照（读路径因此永远看到一致状态）
            _snapshot = new Dictionary<ImgVersion, Func<IImgHandler>>(Factories);
        }
    }

    /// <summary>
    ///     已注册的版本列表（顺序不保证，供诊断面板展示）。
    /// </summary>
    public static IReadOnlyList<ImgVersion> RegisteredVersions
    {
        get => (_snapshot ?? BuildSnapshot()).Keys.ToArray();
    }
}
