using System.Reflection;
using Ralph.Engines.Abstractions;
using Ralph.Engines.Agent;

namespace Ralph.Tests.Engines;

public class EngineArgsTokenizationTests
{
    [Fact]
    public void AgentEngine_BuildArgs_UsesSeparateTokens()
    {
        var engine = new AgentEngine("gemini", "gemini");
        var method = typeof(AgentEngine).GetMethod("BuildArgs", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var request = new EngineRequest
        {
            WorkingDirectory = ".",
            TaskText = "hello world",
            ModelOverride = "gemini-2.5-pro",
            ExtraArgsPassthrough = new[] { "--max-tokens", "1024" }
        };

        var args = (List<string>?)method!.Invoke(engine, new object[] { request });
        Assert.NotNull(args);
        Assert.Contains("--model", args!);
        Assert.Contains("gemini-2.5-pro", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--model ", StringComparison.Ordinal));
    }

    [Fact]
    public void CursorAgentEngine_BuildArgs_UsesSeparateTokens()
    {
        var engine = new AgentEngine("cursor", "cursor-agent");
        var method = typeof(AgentEngine).GetMethod("BuildArgs", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var request = new EngineRequest
        {
            WorkingDirectory = ".",
            TaskText = "hello world",
            ModelOverride = "gpt-5",
            ExtraArgsPassthrough = new[] { "--max-tokens", "1024" }
        };

        var args = (List<string>?)method!.Invoke(engine, new object[] { request });
        Assert.NotNull(args);
        Assert.Contains("--model", args!);
        Assert.Contains("gpt-5", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--model ", StringComparison.Ordinal));
    }

    [Fact]
    public void CodexEngine_BuildArgs_AddsReasoningEffortOverride_WhenMissing()
    {
        var engine = new AgentEngine("codex", "codex");
        var args = BuildArgs(engine, new EngineRequest
        {
            WorkingDirectory = ".",
            TaskText = "hello world",
            ExtraArgsPassthrough = new[] { "--foo", "bar" }
        });

        Assert.Contains("--json", args);
        Assert.Contains("--full-auto", args);
        Assert.DoesNotContain("--dangerously-bypass-approvals-and-sandbox", args);
        Assert.Contains("-c", args);
        Assert.Contains("model_reasoning_effort=\"high\"", args);
    }

    [Fact]
    public void CodexEngine_BuildArgs_UsesDangerousModeOnlyWhenRequested()
    {
        var engine = new AgentEngine("codex", "codex");
        var args = BuildArgs(engine, new EngineRequest
        {
            WorkingDirectory = ".",
            TaskText = "hello world",
            SecurityMode = "dangerous"
        });

        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", args);
        Assert.DoesNotContain("--full-auto", args);
    }

    [Fact]
    public void CodexEngine_BuildArgs_DoesNotDuplicateReasoningEffortOverride_WhenProvided()
    {
        var engine = new AgentEngine("codex", "codex");
        var args = BuildArgs(engine, new EngineRequest
        {
            WorkingDirectory = ".",
            TaskText = "hello world",
            ExtraArgsPassthrough = new[] { "-c", "model_reasoning_effort=\"medium\"" }
        });

        Assert.Contains("model_reasoning_effort=\"medium\"", args);
        Assert.DoesNotContain("model_reasoning_effort=\"high\"", args);
    }

    [Fact]
    public void CodexEngine_BuildArgs_EnablesFastModeViaConfig_WhenRequested()
    {
        var engine = new AgentEngine("codex", "codex");
        var args = BuildArgs(engine, new EngineRequest
        {
            WorkingDirectory = ".",
            TaskText = "hello world",
            Fast = true
        });

        Assert.Contains("service_tier=\"fast\"", args);
        Assert.Contains("features.fast_mode=true", args);
        Assert.Contains("model_reasoning_effort=\"low\"", args);
        Assert.DoesNotContain("model_reasoning_effort=\"high\"", args);
    }

    [Fact]
    public void CodexEngine_BuildArgs_DoesNotDuplicateFastReasoningEffortOverride_WhenProvided()
    {
        var engine = new AgentEngine("codex", "codex");
        var args = BuildArgs(engine, new EngineRequest
        {
            WorkingDirectory = ".",
            TaskText = "hello world",
            Fast = true,
            ExtraArgsPassthrough = new[] { "-c", "model_reasoning_effort=\"high\"" }
        });

        Assert.Contains("service_tier=\"fast\"", args);
        Assert.Contains("features.fast_mode=true", args);
        Assert.Contains("model_reasoning_effort=\"high\"", args);
        Assert.DoesNotContain("model_reasoning_effort=\"low\"", args);
    }

    private static List<string> BuildArgs(AgentEngine engine, EngineRequest request)
    {
        var method = typeof(AgentEngine).GetMethod("BuildArgs", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var args = (List<string>?)method!.Invoke(engine, new object[] { request });
        Assert.NotNull(args);
        return args!;
    }
}
