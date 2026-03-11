using System.Diagnostics;
using Ralph.Persistence.Workspace;

namespace Ralph.Tests.Commands;

public class CliProcessContractTests
{
    [Fact]
    public void About_UnknownFlag_ReturnsError()
    {
        var result = RunCli(Directory.GetCurrentDirectory(), "about --bogus");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--bogus", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Doctor_NonInteractive_DoesNotInitializeGitWithoutYes()
    {
        var dir = CreateTempDir();
        try
        {
            var workspace = new WorkspaceInitializer();
            workspace.Initialize(dir);
            File.WriteAllText(Path.Combine(dir, "PRD.md"), "- [ ] one");

            var result = RunCli(dir, "doctor --non-interactive");

            Assert.DoesNotContain("Git repository initialized", result.Stdout, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(dir, ".git")));
        }
        finally
        {
            SafeDelete(dir);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunCli(string workingDirectory, string arguments, string? pathOverride = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveDotnetExecutablePath(),
            Arguments = $"\"{GetCliDllPath()}\" {arguments}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.Environment["RALPH_UI"] = "none";
        if (pathOverride != null)
            psi.Environment["PATH"] = pathOverride;

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);
        return (process.ExitCode, stdout, stderr);
    }

    private static string GetCliDllPath()
    {
        var root = FindRepositoryRoot();
        var cliBuildPath = Path.Combine(root, "src", "Ralph.Cli", "bin", "Debug", "net8.0", "win-x64", "ralph.dll");
        if (File.Exists(cliBuildPath))
            return cliBuildPath;

        var fallback = Path.Combine(AppContext.BaseDirectory, "ralph.dll");
        if (File.Exists(fallback))
            return fallback;

        throw new FileNotFoundException($"CLI dll not found at {cliBuildPath}");
    }

    private static string ResolveDotnetExecutablePath()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
                return candidate;
        }

        if (OperatingSystem.IsWindows())
        {
            var defaultInstall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            if (File.Exists(defaultInstall))
                return defaultInstall;
        }

        return "dotnet";
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Ralph.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphCliContractTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SafeDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { }
    }
}
