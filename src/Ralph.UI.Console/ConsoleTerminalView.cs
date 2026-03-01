using Ralph.UI.Abstractions;

namespace Ralph.UI.Console;

public sealed class ConsoleTerminalView : ITerminalView
{
    private const string SpinnerPrefix = "[spin] ";
    private bool _spinnerActive;
    private int _spinnerLineLength;

    public void SetStatus(string message)
    {
        if (message.StartsWith(SpinnerPrefix, StringComparison.Ordinal))
        {
            var text = message[SpinnerPrefix.Length..];
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            var line = $"[status] {text}";
            var padded = line.PadRight(Math.Max(line.Length, _spinnerLineLength));
            System.Console.CursorLeft = 0;
            System.Console.Write(padded);
            System.Console.ResetColor();
            _spinnerActive = true;
            _spinnerLineLength = padded.Length;
            return;
        }

        if (_spinnerActive)
        {
            System.Console.WriteLine();
            _spinnerActive = false;
            _spinnerLineLength = 0;
        }

        System.Console.ForegroundColor = ConsoleColor.Cyan;
        System.Console.WriteLine($"[status] {message}");
        System.Console.ResetColor();
    }

    public void SetProgress(int current, int total, string? taskText = null)
    {
        if (_spinnerActive)
        {
            System.Console.WriteLine();
            _spinnerActive = false;
            _spinnerLineLength = 0;
        }

        var suffix = string.IsNullOrWhiteSpace(taskText) ? string.Empty : $" - {taskText}";
        System.Console.WriteLine($"[progress] {current}/{total}{suffix}");
    }

    public void WriteLine(string text)
    {
        if (_spinnerActive)
        {
            System.Console.WriteLine();
            _spinnerActive = false;
            _spinnerLineLength = 0;
        }

        System.Console.WriteLine(text);
    }
    public void Clear() => System.Console.Clear();
}
