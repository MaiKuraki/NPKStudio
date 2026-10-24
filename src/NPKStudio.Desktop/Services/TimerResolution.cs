using System.Runtime.InteropServices;

namespace NPKStudio.Desktop.Services;

/// <summary>
///     临时提高 Windows 定时器分辨率（1ms）。
///     <para>
///         默认分辨率约 15.6ms，<c>DispatcherTimer</c> 的 31ms 间隔会被量化到 ~47ms，
///         导致高倍率播放（2× 以上）实际帧率明显低于标称值。
///         播放期间提升到 1ms，停止播放立刻恢复，避免长期影响系统耗电。
///     </para>
///     <para>
///         多通道可同时播放，所以这里按<b>引用计数</b>持有：第一个播放开始才提升，最后一个停下才恢复。
///     </para>
/// </summary>
internal static class TimerResolution {
    private const uint TargetPeriodMs = 1;

    /// <summary>当前有多少个播放器要求高精度。多通道同时播放时按引用计数持有，最后一个停下才恢复。</summary>
    private static int _depth;

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);

    /// <summary>播放开始时调用（可重入，只会真正生效一次）。</summary>
    public static void Begin() {
        if (!OperatingSystem.IsWindows()) return;
        if (_depth++ > 0) return;

        try {
            TimeBeginPeriod(TargetPeriodMs);
        } catch (DllNotFoundException) {
            // 非 Windows / 缺少 winmm：忽略，退化为系统默认精度
            _depth = 0;
        }
    }

    /// <summary>播放结束时调用（按引用计数递减，归零才真正恢复系统精度）。</summary>
    public static void End() {
        if (_depth == 0) return;
        if (--_depth > 0) return;

        try {
            TimeEndPeriod(TargetPeriodMs);
        } catch (DllNotFoundException) {
            // 同上
        }
    }
}
