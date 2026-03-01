using Ralph.UI.Abstractions;

namespace Ralph.UI.Console;

public sealed class ConsoleInteraction : IUserInteraction
{
    public void WriteInfo(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.White;
        System.Console.WriteLine(message);
        System.Console.ResetColor();
    }

    public void WriteWarn(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Yellow;
        System.Console.WriteLine(message);
        System.Console.ResetColor();
    }

    public void WriteError(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Red;
        System.Console.Error.WriteLine(message);
        System.Console.ResetColor();
    }

    public void WriteVerbose(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.DarkGray;
        System.Console.WriteLine(message);
        System.Console.ResetColor();
    }

    public bool Confirm(string message)
    {
        System.Console.Write($"{message} (y/n): ");
        var line = System.Console.ReadLine()?.Trim().ToLowerInvariant();
        return line == "y" || line == "yes";
    }

    public string? Choose(string title, IReadOnlyList<string> options)
    {
        if (options.Count == 0) return null;
        System.Console.WriteLine(title);
        for (var i = 0; i < options.Count; i++)
            System.Console.WriteLine($"  {i + 1}. {options[i]}");
        System.Console.Write("Choice (1-{0}): ", options.Count);
        var input = System.Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input)) return null;
        if (int.TryParse(input, out var idx) && idx >= 1 && idx <= options.Count)
            return options[idx - 1];
        return null;
    }

    public void ShowProgress(string message, int? percent = null)
    {
        if (percent.HasValue)
            System.Console.WriteLine($"{message} ({percent}%)");
        else
            System.Console.WriteLine(message);
    }
}
