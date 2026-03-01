using Ralph.Core.Config;
using Ralph.Core.Localization;

namespace Ralph.Cli.Commands;

public sealed class UiCommand
{
    private static readonly string[] ToggleOrder = GlobalConfig.ValidUiModes;

    public int Execute(string subCommand, string? arg, IStringCatalog s)
    {
        var config = GlobalConfig.Load();

        switch (subCommand.ToLowerInvariant())
        {
            case "current":
                var currentMode = GlobalConfig.NormalizeUiMode(config.Ui);
                if (!string.Equals(currentMode, config.Ui, StringComparison.Ordinal))
                {
                    config.Ui = currentMode;
                    config.Save();
                }
                Console.WriteLine(s.Format("ui.current", currentMode));
                return 0;

            case "set":
                if (string.IsNullOrWhiteSpace(arg))
                {
                    Console.Error.WriteLine(s.Get("ui.usage_set"));
                    return 1;
                }
                var targetMode = GlobalConfig.NormalizeUiMode(arg);
                if (!GlobalConfig.ValidUiModes.Contains(targetMode, StringComparer.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(s.Format("ui.invalid", arg));
                    return 1;
                }
                config.Ui = targetMode;
                config.Save();
                Console.WriteLine(s.Format("ui.set_ok", config.Ui));
                return 0;

            case "toggle":
                var normalizedCurrent = GlobalConfig.NormalizeUiMode(config.Ui);
                var idx = Array.IndexOf(ToggleOrder, normalizedCurrent);
                if (idx < 0) idx = Array.IndexOf(ToggleOrder, "spectre");
                config.Ui = ToggleOrder[(idx + 1) % ToggleOrder.Length];
                config.Save();
                Console.WriteLine(s.Format("ui.toggled", config.Ui));
                return 0;

            default:
                Console.Error.WriteLine(s.Format("ui.unknown_subcommand", subCommand));
                return 1;
        }
    }
}
