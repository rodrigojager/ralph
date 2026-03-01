using Ralph.Core.RunLoop;
using Ralph.Engines.Abstractions;
using Ralph.Engines.Fake;
using Ralph.Engines.Registry;
using Ralph.Persistence.State;
using Ralph.Persistence.Workspace;
using Ralph.Tasks.Prd;
using Ralph.UI.Console;
using Xunit;

namespace Ralph.Tests.Integration;

public class InitAndRunIntegrationTests
{
    [Fact]
    public async Task Init_creates_ralph_dir_and_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphIntegration_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var init = new WorkspaceInitializer();
            init.Initialize(dir);
            Assert.True(init.IsInitialized(dir));
            Assert.True(File.Exists(init.GetStatePath(dir)));
            Assert.True(File.Exists(init.GetGuardrailsPath(dir)));
            Assert.True(File.Exists(init.GetProgressPath(dir)));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Run_once_with_fake_engine_marks_one_task()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphIntegration_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var prdPath = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prdPath, @"- [ ] First
- [ ] Second");
            var init = new WorkspaceInitializer();
            init.Initialize(dir);
            var registry = new EngineRegistry();
            registry.Register(new FakeEngine("fake", (req, _) =>
            {
                File.WriteAllText(Path.Combine(req.WorkingDirectory, "created-by-engine.txt"), "ok");
                return Task.FromResult(new EngineResult { ExitCode = 0, CompletionSignal = CompletionSignal.Complete });
            }));
            var runLoop = new RunLoopService(registry, new StateStore(), init, new ConsoleInteraction());
            var result = await runLoop.OnceAsync(dir, prdPath, skipTests: true, engineName: "fake");
            Assert.True(result.Completed);
            var doc = PrdParser.Parse(prdPath);
            Assert.True(doc.TaskEntries[0].IsCompleted);
            Assert.False(doc.TaskEntries[1].IsCompleted);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Run_does_not_mark_task_if_engine_makes_no_changes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphIntegration_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var prdPath = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prdPath, "- [ ] First");
            var init = new WorkspaceInitializer();
            init.Initialize(dir);
            var registry = new EngineRegistry();
            registry.Register(new FakeEngine("fake", (req, _) => Task.FromResult(new EngineResult { ExitCode = 0, CompletionSignal = CompletionSignal.Complete })));
            var runLoop = new RunLoopService(registry, new StateStore(), init, new ConsoleInteraction());

            var result = await runLoop.RunAsync(dir, prdPath, maxIterations: 1, skipTests: true, engineName: "fake");

            Assert.False(result.Completed);
            var doc = PrdParser.Parse(prdPath);
            Assert.False(doc.TaskEntries[0].IsCompleted);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Run_ignores_context_gutter_signal_by_default()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphIntegration_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var prdPath = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prdPath, @"- [ ] First
- [ ] Second");
            var init = new WorkspaceInitializer();
            init.Initialize(dir);
            var registry = new EngineRegistry();
            var changeIndex = 0;
            registry.Register(new FakeEngine("fake", (req, _) =>
            {
                changeIndex++;
                File.WriteAllText(Path.Combine(req.WorkingDirectory, $"created-{changeIndex}.txt"), "ok");
                return Task.FromResult(new EngineResult
                {
                    ExitCode = 0,
                    CompletionSignal = CompletionSignal.Complete,
                    Stdout = "GUTTER"
                });
            }));
            var runLoop = new RunLoopService(registry, new StateStore(), init, new ConsoleInteraction());

            var result = await runLoop.RunAsync(dir, prdPath, maxIterations: 2, skipTests: true, engineName: "fake");

            Assert.True(result.Completed);
            Assert.False(result.Gutter);
            var doc = PrdParser.Parse(prdPath);
            Assert.True(doc.TaskEntries[0].IsCompleted);
            Assert.True(doc.TaskEntries[1].IsCompleted);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Run_respects_context_gutter_signal_when_requested()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphIntegration_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var prdPath = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prdPath, @"- [ ] First
- [ ] Second");
            var init = new WorkspaceInitializer();
            init.Initialize(dir);
            var registry = new EngineRegistry();
            registry.Register(new FakeEngine("fake", (req, _) =>
            {
                File.WriteAllText(Path.Combine(req.WorkingDirectory, "created.txt"), "ok");
                return Task.FromResult(new EngineResult
                {
                    ExitCode = 0,
                    CompletionSignal = CompletionSignal.Complete,
                    Stdout = "GUTTER"
                });
            }));
            var runLoop = new RunLoopService(registry, new StateStore(), init, new ConsoleInteraction());

            var result = await runLoop.RunAsync(
                dir,
                prdPath,
                maxIterations: 2,
                skipTests: true,
                engineName: "fake",
                ignoreContextStops: false);

            Assert.False(result.Completed);
            Assert.True(result.Gutter);
            var doc = PrdParser.Parse(prdPath);
            Assert.False(doc.TaskEntries[0].IsCompleted);
            Assert.False(doc.TaskEntries[1].IsCompleted);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Run_ignores_context_defer_signal_by_default()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphIntegration_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var prdPath = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prdPath, @"- [ ] First
- [ ] Second");
            var init = new WorkspaceInitializer();
            init.Initialize(dir);
            var registry = new EngineRegistry();
            var changeIndex = 0;
            registry.Register(new FakeEngine("fake", (req, _) =>
            {
                changeIndex++;
                File.WriteAllText(Path.Combine(req.WorkingDirectory, $"defer-{changeIndex}.txt"), "ok");
                return Task.FromResult(new EngineResult
                {
                    ExitCode = 0,
                    CompletionSignal = CompletionSignal.Complete,
                    Stdout = "DEFER"
                });
            }));
            var runLoop = new RunLoopService(registry, new StateStore(), init, new ConsoleInteraction());

            var result = await runLoop.RunAsync(dir, prdPath, maxIterations: 2, skipTests: true, engineName: "fake");

            Assert.True(result.Completed);
            Assert.False(result.Gutter);
            var doc = PrdParser.Parse(prdPath);
            Assert.True(doc.TaskEntries[0].IsCompleted);
            Assert.True(doc.TaskEntries[1].IsCompleted);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Run_respects_context_defer_signal_when_requested()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphIntegration_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var prdPath = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prdPath, @"- [ ] First
- [ ] Second");
            var init = new WorkspaceInitializer();
            init.Initialize(dir);
            var registry = new EngineRegistry();
            registry.Register(new FakeEngine("fake", (req, _) =>
            {
                File.WriteAllText(Path.Combine(req.WorkingDirectory, "defer.txt"), "ok");
                return Task.FromResult(new EngineResult
                {
                    ExitCode = 0,
                    CompletionSignal = CompletionSignal.Complete,
                    Stdout = "DEFER"
                });
            }));
            var runLoop = new RunLoopService(registry, new StateStore(), init, new ConsoleInteraction());

            var result = await runLoop.RunAsync(
                dir,
                prdPath,
                maxIterations: 2,
                skipTests: true,
                engineName: "fake",
                ignoreContextStops: false);

            Assert.False(result.Completed);
            Assert.False(result.Gutter);
            var doc = PrdParser.Parse(prdPath);
            Assert.False(doc.TaskEntries[0].IsCompleted);
            Assert.False(doc.TaskEntries[1].IsCompleted);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Run_no_change_retry_stops_on_max_retries_by_default()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphIntegration_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var prdPath = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prdPath, "- [ ] First");
            var init = new WorkspaceInitializer();
            init.Initialize(dir);
            var registry = new EngineRegistry();
            var calls = 0;
            registry.Register(new FakeEngine("fake", (req, _) =>
            {
                calls++;
                return Task.FromResult(new EngineResult { ExitCode = 0, CompletionSignal = CompletionSignal.Complete });
            }));
            var runLoop = new RunLoopService(registry, new StateStore(), init, new ConsoleInteraction());

            var result = await runLoop.RunAsync(
                dir,
                prdPath,
                maxIterations: 10,
                skipTests: true,
                engineName: "fake",
                noChangePolicyOverride: "retry",
                noChangeMaxAttemptsOverride: 2);

            Assert.False(result.Completed);
            Assert.Equal(2, calls);
            var doc = PrdParser.Parse(prdPath);
            Assert.False(doc.TaskEntries[0].IsCompleted);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Run_no_change_retry_can_continue_after_max_retries_when_disabled()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphIntegration_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var prdPath = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prdPath, "- [ ] First");
            var init = new WorkspaceInitializer();
            init.Initialize(dir);
            var registry = new EngineRegistry();
            var calls = 0;
            registry.Register(new FakeEngine("fake", (req, _) =>
            {
                calls++;
                return Task.FromResult(new EngineResult { ExitCode = 0, CompletionSignal = CompletionSignal.Complete });
            }));
            var runLoop = new RunLoopService(registry, new StateStore(), init, new ConsoleInteraction());

            var result = await runLoop.RunAsync(
                dir,
                prdPath,
                maxIterations: 3,
                skipTests: true,
                engineName: "fake",
                noChangePolicyOverride: "retry",
                noChangeMaxAttemptsOverride: 2,
                noChangeStopOnMaxAttemptsOverride: false);

            Assert.False(result.Completed);
            Assert.Equal(6, calls);
            var doc = PrdParser.Parse(prdPath);
            Assert.False(doc.TaskEntries[0].IsCompleted);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
