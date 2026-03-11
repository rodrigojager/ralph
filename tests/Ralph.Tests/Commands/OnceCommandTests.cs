using Ralph.Cli.Commands;
using Ralph.Core.RunLoop;
using Ralph.Engines.Abstractions;
using Ralph.Engines.Fake;
using Ralph.Engines.Registry;
using Ralph.Persistence.State;
using Ralph.Persistence.Workspace;
using Ralph.Tasks.Prd;
using Ralph.UI.Console;

namespace Ralph.Tests.Commands;

public class OnceCommandTests
{
    [Fact]
    public async Task Once_DryRun_DoesNotExecuteEngineOrMarkTask()
    {
        var dir = CreateTempDir();
        try
        {
            var prdPath = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prdPath, "- [ ] one");
            var workspace = new WorkspaceInitializer();
            workspace.Initialize(dir);
            var calls = 0;
            var registry = new EngineRegistry();
            registry.Register(new FakeEngine("fake", (_, _) =>
            {
                calls++;
                File.WriteAllText(Path.Combine(dir, "should-not-exist.txt"), "nope");
                return Task.FromResult(new EngineResult { ExitCode = 0, CompletionSignal = CompletionSignal.Complete });
            }));
            var runLoop = new RunLoopService(registry, new StateStore(), workspace, new ConsoleInteraction());
            var command = new OnceCommand(runLoop);

            var exit = await command.ExecuteAsync(dir, prdPath, engine: "fake", dryRun: true);

            Assert.Equal(0, exit);
            Assert.Equal(0, calls);
            Assert.False(File.Exists(Path.Combine(dir, "should-not-exist.txt")));
            Assert.False(PrdParser.Parse(prdPath).TaskEntries[0].IsCompleted);
        }
        finally
        {
            SafeDelete(dir);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphOnceCommandTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SafeDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { }
    }
}
