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

public class ParallelCommandTests
{
    [Fact]
    public async Task Parallel_NoPrd_ReturnsError()
    {
        var dir = CreateTempDir();
        try
        {
            var cmd = BuildCommand(dir);
            var exit = await cmd.ExecuteAsync(dir, Path.Combine(dir, "missing.md"), null, "fake", null, null, null, null, false, false, false, null, StringCatalog.Default());
            Assert.Equal(1, exit);
        }
        finally { SafeDelete(dir); }
    }

    [Fact]
    public async Task Parallel_PrdWithoutPending_ReturnsSuccess()
    {
        var dir = CreateTempDir();
        try
        {
            var prd = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prd, "- [x] done");
            var cmd = BuildCommand(dir);
            var exit = await cmd.ExecuteAsync(dir, prd, null, "fake", null, null, null, null, false, false, false, null, StringCatalog.Default());
            Assert.Equal(0, exit);
        }
        finally { SafeDelete(dir); }
    }

    [Fact]
    public async Task Parallel_MarksTasksAsDone_OnSuccess()
    {
        var dir = CreateTempDir();
        try
        {
            var prd = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prd, "- [ ] one\n- [ ] two");
            var cmd = BuildCommand(dir, (_, _) =>
            {
                File.WriteAllText(Path.Combine(dir, $"ok-{Guid.NewGuid():N}.txt"), "ok");
                return Task.FromResult(new EngineResult { ExitCode = 0, CompletionSignal = CompletionSignal.Complete });
            });

            var exit = await cmd.ExecuteAsync(dir, prd, 2, "fake", null, null, null, null, false, false, false, null, StringCatalog.Default());
            var doc = PrdParser.Parse(prd);
            Assert.Equal(0, exit);
            Assert.All(doc.TaskEntries, t => Assert.True(t.IsCompleted));
        }
        finally { SafeDelete(dir); }
    }

    [Fact]
    public async Task Parallel_RetryFailed_SucceedsOnSecondAttempt()
    {
        var dir = CreateTempDir();
        try
        {
            var prd = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prd, "- [ ] one");
            var calls = 0;
            var cmd = BuildCommand(dir, (_, _) =>
            {
                calls++;
                if (calls == 1)
                    return Task.FromResult(new EngineResult { ExitCode = 2, CompletionSignal = CompletionSignal.None, Stderr = "fail" });
                File.WriteAllText(Path.Combine(dir, "changed.txt"), "ok");
                return Task.FromResult(new EngineResult { ExitCode = 0, CompletionSignal = CompletionSignal.Complete });
            });

            var exit = await cmd.ExecuteAsync(dir, prd, 1, "fake", null, null, null, null, false, false, true, null, StringCatalog.Default());
            Assert.Equal(0, exit);
            Assert.True(PrdParser.Parse(prd).TaskEntries[0].IsCompleted);
        }
        finally { SafeDelete(dir); }
    }

    [Fact]
    public async Task Parallel_InvalidMaxParallel_UsesConfigFallback()
    {
        var dir = CreateTempDir();
        try
        {
            var init = new WorkspaceInitializer();
            init.Initialize(dir);
            var configPath = init.GetConfigPath(dir);
            var store = new ConfigStore();
            var cfg = store.Load(configPath);
            cfg.Parallel ??= new ParallelConfigEntry();
            cfg.Parallel.MaxParallel = 3;
            store.Save(configPath, cfg);

            var prd = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prd, "- [ ] one");
            var cmd = BuildCommand(dir);
            var exit = await cmd.ExecuteAsync(dir, prd, 0, "fake", null, null, null, null, false, true, false, null, StringCatalog.Default());
            Assert.Equal(0, exit);
        }
        finally { SafeDelete(dir); }
    }

    private static ParallelCommand BuildCommand(string dir, Func<EngineRequest, CancellationToken, Task<EngineResult>>? run = null)
    {
        var init = new WorkspaceInitializer();
        init.Initialize(dir);
        var registry = new EngineRegistry();
        registry.Register(new FakeEngine("fake", run));
        var runLoop = new RunLoopService(registry, new StateStore(), init, new ConsoleInteraction());
        return new ParallelCommand(runLoop, init, new ConfigStore());
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphParallelTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SafeDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { }
    }
}
