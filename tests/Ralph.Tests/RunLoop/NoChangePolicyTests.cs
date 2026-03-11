using System.Reflection;
using Ralph.Core.RunLoop;

namespace Ralph.Tests.RunLoop;

public class NoChangePolicyTests
{
    [Fact]
    public void ResolveNoChangeAction_FailFast_ReturnsFailFast()
    {
        var method = typeof(RunLoopService).GetMethod("ResolveNoChangeAction", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var policyType = typeof(RunLoopService).Assembly.GetType("Ralph.Core.RunLoop.NoChangePolicy");
        Assert.NotNull(policyType);
        var failFast = Enum.Parse(policyType!, "FailFast");

        var action = method!.Invoke(null, new object[] { failFast, 0, 3, true, 0, 4 });
        Assert.Equal("FailFast", action?.ToString());
    }

    [Fact]
    public void ResolveNoChangeAction_Fallback_Stops_When_No_Engines_Remain_And_Stop_Is_Enabled()
    {
        var method = typeof(RunLoopService).GetMethod("ResolveNoChangeAction", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var policyType = typeof(RunLoopService).Assembly.GetType("Ralph.Core.RunLoop.NoChangePolicy");
        Assert.NotNull(policyType);
        var fallback = Enum.Parse(policyType!, "Fallback");

        var action = method!.Invoke(null, new object[] { fallback, 0, 3, true, 0, 1 });
        Assert.Equal("FailFast", action?.ToString());
    }

    [Fact]
    public void ResolveNoChangeAction_Fallback_Continues_When_No_Engines_Remain_And_Stop_Is_Disabled()
    {
        var method = typeof(RunLoopService).GetMethod("ResolveNoChangeAction", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var policyType = typeof(RunLoopService).Assembly.GetType("Ralph.Core.RunLoop.NoChangePolicy");
        Assert.NotNull(policyType);
        var fallback = Enum.Parse(policyType!, "Fallback");

        var action = method!.Invoke(null, new object[] { fallback, 0, 3, false, 0, 1 });
        Assert.Equal("Continue", action?.ToString());
    }
}
