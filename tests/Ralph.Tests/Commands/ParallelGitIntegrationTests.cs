using System.Diagnostics;
using Ralph.Cli.Commands;
using Ralph.Core.Localization;
using Ralph.Core.RunLoop;
using Ralph.Engines.Abstractions;
using Ralph.Engines.Fake;
using Ralph.Engines.Registry;
using Ralph.Persistence.Config;
using Ralph.Persistence.State;
using Ralph.Persistence.Workspace;
using Ralph.Tasks.Prd;
using Ralph.UI.Console;

namespace Ralph.Tests.Commands;

public class ParallelGitIntegrationTests
{
    [Fact]
    public async Task Parallel_DoesNotMarkTaskDone_WhenWorktreeCommitFails()
    {
        var dir = CreateTempDir();
        try
        {
            InitializeGitRepository(dir);
            var workspace = new WorkspaceInitializer();
            workspace.Initialize(dir);
            var prdPath = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prdPath, "- [ ] one");
            CreateFailingPreCommitHook(dir);

            var registry = new EngineRegistry();
            registry.Register(new FakeEngine("fake", (request, _) =>
            {
                File.WriteAllText(Path.Combine(request.WorkingDirectory, "created.txt"), "ok");
                return Task.FromResult(new EngineResult { ExitCode = 0, CompletionSignal = CompletionSignal.Complete });
            }));
            var runLoop = new RunLoopService(registry, new StateStore(), workspace, new ConsoleInteraction());
            var command = new ParallelCommand(runLoop, workspace, new ConfigStore());

            var exit = await command.ExecuteAsync(dir, prdPath, 1, "fake", null, null, null, null, false, false, false, null, StringCatalog.Default());

            Assert.Equal(1, exit);
            Assert.False(PrdParser.Parse(prdPath).TaskEntries[0].IsCompleted);
        }
        finally
        {
            SafeDelete(dir);
        }
    }

    private static void InitializeGitRepository(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "seed.txt"), "seed");
        Assert.True(RunGit(dir, "init"));
        Assert.True(RunGit(dir, "config user.email test@example.com"));
        Assert.True(RunGit(dir, "config user.name RalphTest"));
        Assert.True(RunGit(dir, "add -A"));
        Assert.True(RunGit(dir, "commit -m \"init\""));
    }

    private static void CreateFailingPreCommitHook(string dir)
    {
        var hookPath = Path.Combine(dir, ".git", "hooks", "pre-commit");
        File.WriteAllText(hookPath, "#!/bin/sh\r\nexit 1\r\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                hookPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static bool RunGit(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = ResolveGitExecutablePath(),
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        if (process == null)
            return false;
        process.WaitForExit(15_000);
        return process.ExitCode == 0;
    }

    private static string ResolveGitExecutablePath()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "git.exe" : "git");
            if (File.Exists(candidate))
                return candidate;
        }

        if (OperatingSystem.IsWindows())
        {
            var common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe");
            if (File.Exists(common))
                return common;
        }
        else
        {
            foreach (var candidate in new[] { "/usr/bin/git", "/usr/local/bin/git", "/bin/git" })
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return "git";
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphParallelGitTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SafeDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { }
    }
}
