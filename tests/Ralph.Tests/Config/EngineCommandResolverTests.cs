using Ralph.Persistence.Config;

namespace Ralph.Tests.Config;

public class EngineCommandResolverTests
{
    [Fact]
    public void CursorCandidates_IncludeAliases()
    {
        var resolver = new EngineCommandResolver();
        var config = RalphConfig.Default;

        var probe = resolver.Probe("cursor", config);
        var displays = probe.TriedCandidates.Select(c => c.Display()).ToList();

        Assert.Contains("cursor-agent", displays, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("agent", displays, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("cursor agent", displays, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfigCommandWithSubcommand_IsTokenizedAsExecutableAndPrefix()
    {
        var resolver = new EngineCommandResolver();
        var config = RalphConfig.Default;
        config.Engines ??= new Dictionary<string, EngineConfigEntry>(StringComparer.OrdinalIgnoreCase);
        config.Engines["cursor"] = new EngineConfigEntry { Command = "cursor agent" };

        var probe = resolver.Probe("cursor", config);
        var first = probe.TriedCandidates.First();

        Assert.Equal("cursor", first.Executable, ignoreCase: true);
        Assert.Single(first.PrefixArgs);
        Assert.Equal("agent", first.PrefixArgs[0], ignoreCase: true);
    }

    [Fact]
    public void GeminiCandidates_ArePreparedForWindows()
    {
        var resolver = new EngineCommandResolver();
        var probe = resolver.Probe("gemini", RalphConfig.Default);
        var displays = probe.TriedCandidates.Select(c => c.Display()).ToList();

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("gemini.cmd", displays, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("gemini", displays, StringComparer.OrdinalIgnoreCase);
            var first = probe.TriedCandidates[0].Executable;
            Assert.True(
                first.Equals("node", StringComparison.OrdinalIgnoreCase)
                || first.Equals("gemini", StringComparison.OrdinalIgnoreCase),
                $"Unexpected first Gemini candidate: {first}");
        }
        else
        {
            Assert.Contains("gemini", displays, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ResolveForExecution_CursorPrefersCursorAgentFallback_WhenNothingIsCallable()
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", string.Empty);
            var resolver = new EngineCommandResolver();
            var config = RalphConfig.Default;
            config.Engines ??= new Dictionary<string, EngineConfigEntry>(StringComparer.OrdinalIgnoreCase);
            config.Engines["cursor"] = new EngineConfigEntry { Command = "cursor-agent" };

            var resolved = resolver.ResolveForExecution("cursor", config);

            Assert.Equal("cursor", resolved.Executable, ignoreCase: true);
            Assert.Single(resolved.PrefixArgs);
            Assert.Equal("agent", resolved.PrefixArgs[0], ignoreCase: true);
            Assert.False(resolved.Available);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public void ResolveForExecution_GeminiFallsBackToGemini_WhenNothingIsCallable()
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", string.Empty);
            var resolver = new EngineCommandResolver();
            var resolved = resolver.ResolveForExecution("gemini", RalphConfig.Default);

            if (OperatingSystem.IsWindows())
                Assert.True(
                    resolved.Executable.Equals("gemini", StringComparison.OrdinalIgnoreCase)
                    || resolved.Executable.Equals("gemini.cmd", StringComparison.OrdinalIgnoreCase));
            else
                Assert.Equal("gemini", resolved.Executable, ignoreCase: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }
}
