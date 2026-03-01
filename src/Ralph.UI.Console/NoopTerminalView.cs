using Ralph.UI.Abstractions;

namespace Ralph.UI.Console;

public sealed class NoopTerminalView : ITerminalView
{
    public void SetStatus(string message) { }
    public void SetProgress(int current, int total, string? taskText = null) { }
    public void WriteLine(string text) { }
    public void Clear() { }
}
