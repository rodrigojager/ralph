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
}
