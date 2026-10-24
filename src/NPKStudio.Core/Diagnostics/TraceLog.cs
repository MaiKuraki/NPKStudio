using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NPKStudio.Core.Diagnostics;

/// <summary>
///     默认 <see cref="ILog" /> 实现：写入 <see cref="Debug.WriteLine(string)" />，
///     同时保留最近 500 条记录（环形缓冲）供"诊断 → 导出日志"使用。
///     <para>线程安全：写入与快照都在同一把锁内完成；不写 <c>Console</c>（WinExe 无控制台）。</para>
/// </summary>
public sealed class TraceLog : ILog
{
    /// <summary>环形缓冲容量：500 条。</summary>
    public const int Capacity = 500;

    private readonly object _gate = new();
    private readonly string?[] _entries = new string?[Capacity];
    private int _count;
    private int _next;

    /// <summary>
    ///     全局共享实例。UI 与 Core 默认使用它；需要重定向时向使用方注入其它 <see cref="ILog" />。
    /// </summary>
    public static TraceLog Instance { get; } = new();

    /// <summary>
    ///     当前已缓存的记录条数（不超过 <see cref="Capacity" />）。
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <inheritdoc />
    public void Info(string message, Exception? exception = null)
    {
        Write("INFO", message, exception);
    }

    /// <inheritdoc />
    public void Warn(string message, Exception? exception = null)
    {
        Write("WARN", message, exception);
    }

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    /// <summary>
    ///     按时间顺序导出全部缓存记录（最早的在前），每行形如
    ///     <c>2024-05-01 12:00:00.000 [WARN] 消息 - 异常</c>。
    /// </summary>
    /// <returns>按时间升序排列的记录快照，无记录时返回空数组。</returns>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            var result = new List<string>(_count);
            var start = _count < Capacity ? 0 : _next;
            for (var i = 0; i < _count; i++)
            {
                var entry = _entries[(start + i) % Capacity];
                if (entry is not null)
                {
                    result.Add(entry);
                }
            }

            return result;
        }
    }

    /// <summary>
    ///     把缓存记录拼成单个多行字符串，便于一次性写入磁盘或剪贴板。
    /// </summary>
    /// <returns>以 <see cref="Environment.NewLine" /> 分隔的日志文本。</returns>
    public string Dump()
    {
        var builder = new StringBuilder();
        foreach (var line in Snapshot())
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     清空环形缓冲。
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_entries, 0, _entries.Length);
            _count = 0;
            _next = 0;
        }
    }

    private void Write(string level, string message, Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(message);

        var line = Format(level, message, exception);
        Debug.WriteLine(line);

        lock (_gate)
        {
            _entries[_next] = line;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity)
            {
                _count++;
            }
        }
    }

    private static string Format(string level, string message, Exception? exception)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        return exception is null
            ? $"{timestamp} [{level}] {message}"
            : $"{timestamp} [{level}] {message} - {exception.GetType().Name}: {exception.Message}";
    }
}
