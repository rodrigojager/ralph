using Ralph.UI.Abstractions;
using Spectre.Console;

namespace Ralph.UI.Spectre;

public sealed class SpectreInteraction : IUserInteraction
{
    private readonly IUserInteraction _fallback;

    public SpectreInteraction(IUserInteraction fallback)
    {
        _fallback = fallback;
    }

    public void WriteInfo(string message)    => WriteStyled(message, Style.Parse("white"));
    public void WriteWarn(string message)    => WriteStyled(message, Style.Parse("yellow"));
    public void WriteError(string message)   => WriteStyled(message, Style.Parse("bold red"));
    public void WriteVerbose(string message) => WriteStyled(message, Style.Parse("grey"));

    public void ShowProgress(string message, int? percent = null)
    {
        if (percent.HasValue)
            WriteStyled($"{message} ({percent}%)", Style.Parse("cyan"));
        else
            WriteStyled(message, Style.Parse("cyan"));
    }

    public bool Confirm(string prompt)
    {
        try
        {
            return AnsiConsole.Confirm(Escape(prompt));
        }
        catch
        {
            return _fallback.Confirm(prompt);
        }
    }

    public string? Choose(string prompt, IReadOnlyList<string> options)
    {
        try
        {
            return AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(Escape(prompt))
                    .UseConverter(o => Escape(o))
                    .AddChoices(options));
        }
        catch
        {
            return _fallback.Choose(prompt, options);
        }
    }

    private static void WriteStyled(string message, Style style)
    {
        AnsiConsole.Write(new Text(message, style));
        AnsiConsole.WriteLine();
    }

    private static string Escape(string s) => s
        .Replace("[", "[[")
        .Replace("]", "]]");
}
