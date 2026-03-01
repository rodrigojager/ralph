using System.Collections.Concurrent;
using Ralph.UI.Abstractions;

namespace Ralph.UI.Tui;

public sealed class TuiTerminalView : ITerminalView
{
    private readonly ITerminalView _fallback;
    private readonly Func<bool>? _isHealthy;
    private int _queuedOutputLines;
    private const int MaxQueuedOutputLines = 2000;

    public TuiTerminalView(ITerminalView fallback, Func<bool>? isHealthy = null)
    {
        _fallback = fallback;
        _isHealthy = isHealthy;
    }

    internal string CurrentStatus { get; private set; } = string.Empty;
    internal int ProgressCurrent { get; private set; }
    internal int ProgressTotal { get; private set; }
    internal string? ProgressTask { get; private set; }
    internal readonly ConcurrentQueue<string> OutputLines = new();

    public void SetStatus(string message)
    {
        CurrentStatus = message;
        if (!IsHealthy()) _fallback.SetStatus(message);
    }

    public void SetProgress(int current, int total, string? taskText = null)
    {
        ProgressCurrent = current;
        ProgressTotal = total;
        ProgressTask = taskText;
        if (!IsHealthy()) _fallback.SetProgress(current, total, taskText);
    }

    public void WriteLine(string text)
    {
        OutputLines.Enqueue(text);
        Interlocked.Increment(ref _queuedOutputLines);
        while (Volatile.Read(ref _queuedOutputLines) > MaxQueuedOutputLines && OutputLines.TryDequeue(out _))
            Interlocked.Decrement(ref _queuedOutputLines);
        if (!IsHealthy()) _fallback.WriteLine(text);
    }

    public void Clear()
    {
        // TUI does not clear the full dashboard.
        if (!IsHealthy()) _fallback.Clear();
    }

    private bool IsHealthy() => _isHealthy?.Invoke() ?? false;

    internal bool TryDequeueOutput(out string line)
    {
        if (OutputLines.TryDequeue(out line!))
        {
            Interlocked.Decrement(ref _queuedOutputLines);
            return true;
        }

        line = string.Empty;
        return false;
    }
}
