using System.Reflection;

namespace Ralph.Tests.Engines;

public class StreamJsonOutputParserTests
{
    [Fact]
    public void Parse_DetectsStructuredError()
    {
        var enginesAssembly = typeof(Ralph.Engines.Agent.AgentEngine).Assembly;
        var parserType = enginesAssembly.GetType("Ralph.Engines.Runtime.StreamJsonOutputParser");
        Assert.NotNull(parserType);

        var method = parserType!.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var payload = "{\"type\":\"error\",\"message\":\"boom\"}";
        var result = method!.Invoke(null, new object[] { payload });
        Assert.NotNull(result);

        var hasErrorProp = result!.GetType().GetProperty("HasStructuredError");
        Assert.NotNull(hasErrorProp);
        var hasError = (bool)hasErrorProp!.GetValue(result)!;
        Assert.True(hasError);
    }
}
