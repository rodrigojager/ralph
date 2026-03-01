namespace Ralph.UI.Abstractions;

/// <summary>
/// Abstraction for rich terminal UI (e.g. terminal.gui plugin).
/// Implementations: console (default), noop (headless), and optional terminal.gui plugin.
/// </summary>
public interface ITerminalView
{
    void SetStatus(string message);
    void SetProgress(int current, int total, string? taskText = null);
    void WriteLine(string text);
    void Clear();
}
