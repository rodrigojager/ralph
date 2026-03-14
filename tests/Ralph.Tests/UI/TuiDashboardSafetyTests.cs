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

    [Fact]
    public void GetDebugLogPath_PointsInsideWorkspace()
    {
        var method = typeof(TuiDashboard).GetMethod("GetDebugLogPath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var original = Directory.GetCurrentDirectory();
        var dir = Path.Combine(Path.GetTempPath(), "RalphTuiTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            Directory.SetCurrentDirectory(dir);

            var path = (string?)method!.Invoke(null, []);

            Assert.NotNull(path);
            Assert.EndsWith(Path.Combine(".ralph", "tui-debug.log"), path!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
