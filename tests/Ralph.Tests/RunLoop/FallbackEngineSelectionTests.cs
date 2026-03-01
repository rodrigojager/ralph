using System.Reflection;
using Ralph.Core.RunLoop;
using Ralph.Persistence.Config;

namespace Ralph.Tests.RunLoop;

public class FallbackEngineSelectionTests
{
    [Fact]
    public void BuildEngineCandidates_CursorWithoutConfiguredFallbacks_DoesNotInjectDefaults()
    {
        var method = typeof(RunLoopService).GetMethod("BuildEngineCandidates", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var cfg = RalphConfig.Default;
        cfg.FallbackEngines = new List<string>();

        var candidates = (List<string>?)method!.Invoke(null, new object[] { "cursor", cfg });
        Assert.NotNull(candidates);
        Assert.Equal(new[] { "cursor" }, candidates!, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildEngineCandidates_UsesOnlyUserConfiguredFallbacks()
    {
        var method = typeof(RunLoopService).GetMethod("BuildEngineCandidates", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var cfg = RalphConfig.Default;
        cfg.FallbackEngines = new List<string> { "gemini", "codex" };

        var candidates = (List<string>?)method!.Invoke(null, new object[] { "cursor", cfg });
        Assert.NotNull(candidates);
        Assert.Equal(new[] { "cursor", "gemini", "codex" }, candidates!, StringComparer.OrdinalIgnoreCase);
    }
}
