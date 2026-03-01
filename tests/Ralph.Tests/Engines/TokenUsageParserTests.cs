using System.Reflection;

namespace Ralph.Tests.Engines;

public class TokenUsageParserTests
{
    [Fact]
    public void Parse_ReadsCamelCaseUsageFields()
    {
        var parse = GetParseMethod();
        var stdout = """{"usage":{"inputTokens":1200,"outputTokens":345,"totalTokens":1545}}""";

        var usage = parse.Invoke(null, new object[] { stdout, string.Empty, "cursor" });

        Assert.NotNull(usage);
        Assert.Equal(1200, ReadInt(usage!, "InputTokens"));
        Assert.Equal(345, ReadInt(usage!, "OutputTokens"));
    }

    [Fact]
    public void Parse_ReadsCodexTokensUsedBlock_WithThousandsSeparator()
    {
        var parse = GetParseMethod();
        var stdout = """
tokens used
6.266
""";

        var usage = parse.Invoke(null, new object[] { stdout, string.Empty, "codex" });

        Assert.NotNull(usage);
        Assert.Equal(0, ReadInt(usage!, "InputTokens"));
        Assert.Equal(6266, ReadInt(usage!, "OutputTokens"));
    }

    private static MethodInfo GetParseMethod()
    {
        var enginesAssembly = typeof(Ralph.Engines.Agent.AgentEngine).Assembly;
        var parserType = enginesAssembly.GetType("Ralph.Engines.Tokens.TokenUsageParser");
        Assert.NotNull(parserType);

        var parse = parserType!.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(parse);
        return parse!;
    }

    private static int ReadInt(object usage, string propertyName)
    {
        var prop = usage.GetType().GetProperty(propertyName);
        Assert.NotNull(prop);
        return (int)prop!.GetValue(usage)!;
    }
}
