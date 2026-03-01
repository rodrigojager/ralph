using Ralph.Cli.Commands;
using Ralph.Core.Config;
using Ralph.Core.Localization;

namespace Ralph.Tests.Commands;

public class UiCommandTests
{
    private static readonly object ConfigLock = new();

    [Fact]
    public void Toggle_CyclesIncludingTui()
    {
        lock (ConfigLock)
        {
            var path = GlobalConfig.ConfigPath();
            var backup = Backup(path);
            try
            {
                var cfg = GlobalConfig.Load();
                cfg.Ui = "spectre+gum";
                cfg.Save();

                var cmd = new UiCommand();
                var exit = cmd.Execute("toggle", null, StringCatalog.Default());

                var current = GlobalConfig.Load().Ui;
                Assert.Equal(0, exit);
                Assert.Equal("tui", current);
            }
            finally
            {
                Restore(path, backup);
            }
        }
    }

    [Fact]
    public void Current_NormalizesInvalidUiValue()
    {
        lock (ConfigLock)
        {
            var path = GlobalConfig.ConfigPath();
            var backup = Backup(path);
            try
            {
                var cfg = GlobalConfig.Load();
                cfg.Ui = "invalid-ui";
                cfg.Save();

                var cmd = new UiCommand();
                var exit = cmd.Execute("current", null, StringCatalog.Default());

                var current = GlobalConfig.Load().Ui;
                Assert.Equal(0, exit);
                Assert.Equal("spectre", current);
            }
            finally
            {
                Restore(path, backup);
            }
        }
    }

    private static string? Backup(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static void Restore(string path, string? backup)
    {
        if (backup == null)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        File.WriteAllText(path, backup);
    }
}
