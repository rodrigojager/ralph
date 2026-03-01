using System.Reflection;
using Ralph.UI.Tui;

namespace Ralph.Tests.UI;

public class TuiDashboardSafetyTests
{
    [Fact]
    public void SanitizeListItem_ReplacesEmptyWithSpace()
    {
        var method = typeof(TuiDashboard).GetMethod("SanitizeListItem", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = (string?)method!.Invoke(null, new object[] { string.Empty });

        Assert.Equal(" ", value);
    }

    [Fact]
    public void IsListViewStartIndexException_DetectsExpectedError()
    {
        var method = typeof(TuiDashboard).GetMethod("IsListViewStartIndexException", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var ex = new ArgumentOutOfRangeException("startIndex");
        var result = (bool)method!.Invoke(null, new object[] { ex })!;

        Assert.True(result);
    }
}
