using System.Collections.Concurrent;
using Ralph.UI.Abstractions;

namespace Ralph.UI.Tui;

public sealed class TuiInteraction : IUserInteraction
{
    private readonly IUserInteraction _fallback;
    private readonly Func<bool>? _isHealthy;
    internal readonly ConcurrentQueue<(string Level, string Message)> MessageQueue = new();

    public TuiInteraction(IUserInteraction fallback, Func<bool>? isHealthy = null)
    {
        _fallback = fallback;
        _isHealthy = isHealthy;
    }

    public void WriteInfo(string message)
    {
        MessageQueue.Enqueue(("info", message));
        if (!IsHealthy()) _fallback.WriteInfo(message);
    }

    public void WriteWarn(string message)
    {
        MessageQueue.Enqueue(("warn", message));
        if (!IsHealthy()) _fallback.WriteWarn(message);
    }

    public void WriteError(string message)
    {
        MessageQueue.Enqueue(("error", message));
        if (!IsHealthy()) _fallback.WriteError(message);
    }

    public void WriteVerbose(string message)
    {
        MessageQueue.Enqueue(("verbose", message));
        if (!IsHealthy()) _fallback.WriteVerbose(message);
    }

    public void ShowProgress(string message, int? percent = null)
    {
        var text = percent.HasValue ? $"{message} ({percent}%)" : message;
        MessageQueue.Enqueue(("progress", text));
        if (!IsHealthy()) _fallback.ShowProgress(message, percent);
    }

    public bool Confirm(string message) => _fallback.Confirm(message);

    public string? Choose(string title, IReadOnlyList<string> options)
        => _fallback.Choose(title, options);

    private bool IsHealthy() => _isHealthy?.Invoke() ?? false;
}
