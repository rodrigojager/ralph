using System.Diagnostics;
using Ralph.UI.Abstractions;

namespace Ralph.UI.Gum;

public sealed class GumInteraction : IUserInteraction
{
    private readonly IUserInteraction _fallback;
    private readonly Lazy<string?> _gumPath;

    public GumInteraction(IUserInteraction fallback)
    {
        _fallback = fallback;
        _gumPath  = new Lazy<string?>(() => FindGumInPath());
    }

    private static string? FindGumInPath()
    {
        var name = OperatingSystem.IsWindows() ? "gum.exe" : "gum";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir, name);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private string? GumExe => _gumPath.Value;

    // ── IUserInteraction ──────────────────────────────────────────────────────

    public void WriteInfo(string message)
    {
        // gum format: pipe message to stdin, let styled output go straight to the terminal (no stdout redirect)
        if (GumExe != null && TryRunGumFormat(GumExe, message))
            return;
        _fallback.WriteInfo(message);
    }

    public void WriteWarn(string message)    => _fallback.WriteWarn(message);
    public void WriteError(string message)   => _fallback.WriteError(message);
    public void WriteVerbose(string message) => _fallback.WriteVerbose(message);
    public void ShowProgress(string message, int? percent = null) => _fallback.ShowProgress(message, percent);

    public bool Confirm(string message)
    {
        // gum confirm: renders TUI to terminal, result is exit code (0 = yes)
        if (GumExe != null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName        = GumExe,
                    UseShellExecute = false,
                    CreateNoWindow  = false   // must be false so GUM can access the console
                };
                psi.ArgumentList.Add("confirm");
                psi.ArgumentList.Add(message);

                using var p = Process.Start(psi)!;
                p.WaitForExit(30_000);
                return p.ExitCode == 0;
            }
            catch { }
        }
        return _fallback.Confirm(message);
    }

    public string? Choose(string title, IReadOnlyList<string> options)
    {
        if (GumExe != null && options.Count > 0)
        {
            // gum choose: when stdout is redirected GUM auto-switches its TUI rendering to stderr.
            // stderr stays attached to the real terminal → user sees the menu.
            // stdout (redirected) carries only the selected value → we capture it.
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = GumExe,
                    UseShellExecute        = false,
                    CreateNoWindow         = false,  // console must be accessible for stdin/stderr
                    RedirectStandardOutput = true    // capture selection; GUM renders TUI to stderr
                };
                psi.ArgumentList.Add("choose");
                psi.ArgumentList.Add("--header");
                psi.ArgumentList.Add(title);
                foreach (var opt in options)
                    psi.ArgumentList.Add(opt);

                using var p = Process.Start(psi)!;
                var chosen = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(60_000);

                if (!string.IsNullOrEmpty(chosen))
                    return options.FirstOrDefault(o => o == chosen);
            }
            catch { }
        }
        return _fallback.Choose(title, options);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TryRunGumFormat(string gumExe, string message)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = gumExe,
                UseShellExecute        = false,
                CreateNoWindow         = false,
                RedirectStandardInput  = true  // pipe message in; let styled output go to terminal
            };
            psi.ArgumentList.Add("format");

            using var p = Process.Start(psi)!;
            p.StandardInput.WriteLine(message);
            p.StandardInput.Close();
            p.WaitForExit(3_000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
