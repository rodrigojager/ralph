using Ralph.Core.Config;
using Ralph.Core.Localization;
using Spectre.Console;

namespace Ralph.Cli.Commands;

public sealed class InstallCommand
{
    public int Execute(string? targetDir = null)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            AnsiConsole.MarkupLine("[red]Could not determine executable path.[/]");
            return 1;
        }

        // ── 1. Language (Spectre arrow-key picker — always works) ─────────────
        var lang = AskLanguage();
        var s = StringCatalog.Load(lang);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(s.Get("install.welcome"))}[/]");
        AnsiConsole.WriteLine();

        // ── 2. GUM detection (informational only) ─────────────────────────────
        var gumPath = FindInPath("gum");
        if (gumPath != null)
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(s.Format("install.gum.detected", gumPath))}[/]");
        else
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(s.Get("install.gum.not_found"))}[/]");

        // ── 3. Install directory ──────────────────────────────────────────────
        var defaultDir = GetDefaultInstallDir();
        string target;
        if (targetDir != null)
        {
            target = targetDir;
        }
        else
        {
            AnsiConsole.WriteLine();
            target = AnsiConsole.Prompt(
                new TextPrompt<string>(Markup.Escape(s.Get("install.dir.question")))
                    .DefaultValue(defaultDir)
                    .AllowEmpty());
            if (string.IsNullOrEmpty(target)) target = defaultDir;
        }
        if (!Path.IsPathRooted(target))
            target = Path.GetFullPath(target);

        // ── 4. UI mode selection ──────────────────────────────────────────────
        AnsiConsole.WriteLine();
        var uiMode = AskUiMode(s);
        var gumAvailable = gumPath != null;

        // ── 5. GUM install offer (only when user chose a GUM mode but GUM is absent) ──
        if (!gumAvailable && (uiMode == "gum" || uiMode == "spectre+gum"))
        {
            AnsiConsole.WriteLine();
            if (AnsiConsole.Confirm(Markup.Escape(s.Get("install.gum.offer")), defaultValue: false))
            {
                AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(s.Get("install.gum.installing"))}[/]");
                gumAvailable = TryInstallGum();
                AnsiConsole.MarkupLine(gumAvailable
                    ? $"[green]{Markup.Escape(s.Get("install.gum.install_ok"))}[/]"
                    : $"[red]{Markup.Escape(s.Get("install.gum.install_fail"))}[/]");
                if (!gumAvailable)
                    uiMode = uiMode.Replace("gum", "").TrimEnd('+').TrimStart('+');
            }
            else
            {
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(s.Get("install.gum.skip"))}[/]");
                uiMode = uiMode.Replace("gum", "").TrimEnd('+').TrimStart('+');
            }
            if (string.IsNullOrEmpty(uiMode)) uiMode = "spectre";
        }

        // ── 6. Copy binary ────────────────────────────────────────────────────
        var destName = OperatingSystem.IsWindows() ? "ralph.exe" : "ralph";
        var destPath = Path.Combine(target, destName);
        var sourcePath = GetInstallableExecutablePath(exePath);
        if (sourcePath == null)
        {
            AnsiConsole.MarkupLine("[red]Could not determine executable to install. Run install from a published build (e.g. dotnet publish then run the produced ralph.exe).[/]");
            return 1;
        }
        try
        {
            AnsiConsole.WriteLine();
            Directory.CreateDirectory(target);
            var sourceDir = Path.GetDirectoryName(sourcePath)!;

            var isSelf = string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(destPath),
                StringComparison.OrdinalIgnoreCase);

            if (isSelf)
            {
                // Already running from the install location — skip binary copy
                // (cannot overwrite a running exe on Windows).
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(s.Format("install.copying", destPath))} (already in place, skipped)[/]");
            }
            else
            {
                AnsiConsole.MarkupLine(Markup.Escape(s.Format("install.copying", destPath)));
                CopyRuntimePayload(sourceDir, target);
            }

            if (OperatingSystem.IsWindows())
                WriteWindowsLauncher(target, destName);

            CopyLangFiles(target, sourceDir);

            var config     = new GlobalConfig
            {
                Lang = lang,
                Ui = uiMode,
                InstallDir = target,
                ReleaseRepo = ReleaseChannel.ResolveRepo()
            };
            var configPath = Path.Combine(target, "ralph-config.json");
            File.WriteAllText(configPath,
                System.Text.Json.JsonSerializer.Serialize(config,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            AnsiConsole.MarkupLine($"[green]{Markup.Escape(s.Get("install.config_saved"))}[/]");
            AnsiConsole.MarkupLine($"[bold green]{Markup.Escape(s.Get("install.ok"))}[/]");
            AnsiConsole.WriteLine();

            if (TryAddToPath(target))
                AnsiConsole.MarkupLine($"[green]{Markup.Escape(OperatingSystem.IsWindows() ? s.Get("install.path_added") : s.Get("install.path_added_shell"))}[/]");
            else
                PrintPathInstructions(target, s);

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(s.Format("install.fail", ex.Message))}[/]");
            return 1;
        }
    }

    // ── Spectre.Console prompts ───────────────────────────────────────────────

    private static string AskLanguage()
    {
        var result = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]Choose your language / Escolha seu idioma[/]")
                .AddChoices("English", "Português (Brasil)"));
        return result == "Português (Brasil)" ? "ptBR" : "en";
    }

    private static string AskUiMode(IStringCatalog s)
    {
        var spectre = s.Get("install.ui.spectre");
        var none    = s.Get("install.ui.none");
        var gum     = s.Get("install.ui.gum");
        var both    = s.Get("install.ui.spectre_gum");
        const string tui = "TUI — full-screen dashboard (Terminal.Gui)";

        var result = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Markup.Escape(s.Get("install.ui.question")))
                .AddChoices(spectre, tui, none, gum, both));

        if (result == none) return "none";
        if (result == gum)  return "gum";
        if (result == both) return "spectre+gum";
        if (result == tui)  return "tui";
        return "spectre";
    }

    // ── Path & file helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the path of the executable file to copy. If the process was started from a .dll
    /// (e.g. dotnet run), returns the matching .exe in the same directory so we install the host, not the dll.
    /// </summary>
    private static string? GetInstallableExecutablePath(string exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            return null;
        var ext = Path.GetExtension(exePath);
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return exePath;
        if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(exePath);
            var name = Path.GetFileNameWithoutExtension(exePath) + ".exe";
            var hostExe = dir == null ? name : Path.Combine(dir, name);
            if (File.Exists(hostExe))
                return hostExe;
            // No host exe (e.g. self-contained single-file was run as dll); copy the dll as ralph.exe is wrong.
            // Prefer failing so user runs from a proper publish.
            return null;
        }
        return exePath;
    }

    private static string? FindInPath(string binary)
    {
        var name    = OperatingSystem.IsWindows() ? binary + ".exe" : binary;
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir, name);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static bool TryInstallGum()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (TryRunCommand("winget", "install charmbracelet.gum")) return true;
                if (TryRunCommand("scoop",  "install gum"))               return true;
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (TryRunCommand("brew", "install gum")) return true;
            }
            else
            {
                if (TryRunCommand("snap", "install gum"))                                  return true;
                if (TryRunCommand("go",   "install github.com/charmbracelet/gum@latest")) return true;
            }
            return false;
        }
        catch { return false; }
    }

    private static bool TryRunCommand(string fileName, string args)
    {
        try
        {
            using var p = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = fileName,
                    Arguments              = args,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                }
            };
            p.Start();
            p.WaitForExit(60_000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void CopyLangFiles(string targetDir, string sourceBaseDir)
    {
        var srcLang = Path.Combine(sourceBaseDir, "lang");
        var dstLang = Path.Combine(targetDir, "lang");
        if (!Directory.Exists(srcLang)) return;

        var samePath = string.Equals(Path.GetFullPath(srcLang), Path.GetFullPath(dstLang),
            StringComparison.OrdinalIgnoreCase);

        // Running from the install location — source == destination.
        // Files are already up-to-date; nothing to copy.
        if (samePath) return;

        Directory.CreateDirectory(dstLang);
        foreach (var file in Directory.GetFiles(srcLang, "*.json"))
            File.Copy(file, Path.Combine(dstLang, Path.GetFileName(file)), overwrite: true);
    }

    private static void CopyRuntimePayload(string sourceDir, string targetDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                continue;
            var destination = Path.Combine(targetDir, name);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static string GetDefaultInstallDir()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Ralph", "bin");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "bin");
    }

    private static bool TryAddToPath(string dir)
    {
        if (OperatingSystem.IsWindows())
        {
            var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            var entries = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (entries.Any(p => string.Equals(p.Trim(), dir, StringComparison.OrdinalIgnoreCase)))
                return true;
            // Put Ralph first so a stale launcher elsewhere does not shadow it.
            var updated = dir + ";" + current.TrimStart(';');
            Environment.SetEnvironmentVariable("PATH", updated, EnvironmentVariableTarget.User);
            return true;
        }

        var home       = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var exportLine = $"\nexport PATH=\"$PATH:{dir}\"";
        var rcFiles    = new[] { Path.Combine(home, ".zshrc"), Path.Combine(home, ".bashrc") };
        var appended   = false;
        foreach (var rc in rcFiles)
        {
            if (!File.Exists(rc)) continue;
            var content = File.ReadAllText(rc);
            if (content.Contains(dir)) { appended = true; continue; }
            File.AppendAllText(rc, exportLine);
            AnsiConsole.MarkupLine($"  → updated [underline]{Markup.Escape(rc)}[/]");
            appended = true;
        }
        if (!appended)
        {
            var profile = Path.Combine(home, ".profile");
            var content = File.Exists(profile) ? File.ReadAllText(profile) : "";
            if (!content.Contains(dir))
            {
                File.AppendAllText(profile, exportLine);
                AnsiConsole.MarkupLine($"  → updated [underline]{Markup.Escape(profile)}[/]");
            }
            appended = true;
        }
        return appended;
    }

    private static void PrintPathInstructions(string dir, IStringCatalog s)
    {
        AnsiConsole.WriteLine();
        if (OperatingSystem.IsWindows())
        {
            AnsiConsole.MarkupLine(Markup.Escape(s.Get("install.path_manual_win")));
            AnsiConsole.MarkupLine($"  [underline]{Markup.Escape(Path.GetFullPath(dir))}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine(Markup.Escape(s.Get("install.path_manual_unix")));
            AnsiConsole.MarkupLine($"  [italic]{Markup.Escape(s.Format("install.path_export", Path.GetFullPath(dir)))}[/]");
        }
    }

    private static void WriteWindowsLauncher(string targetDir, string exeName)
    {
        var cmdPath = Path.Combine(targetDir, "ralph.cmd");
        var launcher = "@echo off\r\n" +
                       "setlocal\r\n" +
                       "set \"RALPH_EXE=%~dp0" + exeName + "\"\r\n" +
                       "if not exist \"%RALPH_EXE%\" (\r\n" +
                       "  echo Ralph executable not found: \"%RALPH_EXE%\"\r\n" +
                       "  exit /b 1\r\n" +
                       ")\r\n" +
                       "\"%RALPH_EXE%\" %*\r\n";
        File.WriteAllText(cmdPath, launcher);
    }
}
