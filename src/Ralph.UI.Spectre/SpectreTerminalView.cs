using Ralph.UI.Abstractions;
using Spectre.Console;

namespace Ralph.UI.Spectre;

public sealed class SpectreTerminalView : ITerminalView
{
    private const string SpinnerPrefix = "[spin] ";
    private string _status = "";
    private int _current;
    private int _total;
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

        _status = message;
        AnsiConsole.Write(new Text($"[status] {message}", Style.Parse("bold cyan")));
        AnsiConsole.WriteLine();
    }

    public void SetProgress(int current, int total, string? taskText = null)
    {
        if (_spinnerActive)
        {
            System.Console.WriteLine();
            _spinnerActive = false;
            _spinnerLineLength = 0;
        }

        _current = current;
        _total = total;
        var bar = total > 0 ? $"[{current}/{total}]" : "";
        var task = taskText != null ? $" {taskText}" : "";
        AnsiConsole.Write(new Text($"{bar}{task}", Style.Parse("green")));
        AnsiConsole.WriteLine();
    }

    public void WriteLine(string text)
    {
        if (_spinnerActive)
        {
            System.Console.WriteLine();
            _spinnerActive = false;
            _spinnerLineLength = 0;
        }

        // Use Text to avoid Spectre markup parsing on raw engine output.
        AnsiConsole.Write(new Text(text, Style.Plain));
        AnsiConsole.WriteLine();
    }

    public void Clear() => AnsiConsole.Clear();
}
