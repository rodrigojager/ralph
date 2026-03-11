using Ralph.Cli.Infrastructure;

namespace Ralph.Tests.Commands;

public class CurrentExecutableLocatorTests
{
    [Fact]
    public void Resolve_PrefersAppHostBesideAssembly()
    {
        var dir = CreateTempDir();
        try
        {
            var assemblyPath = Path.Combine(dir, "ralph.dll");
            var appHostPath = Path.Combine(dir, OperatingSystem.IsWindows() ? "ralph.exe" : "ralph");
            File.WriteAllText(assemblyPath, "dll");
            File.WriteAllText(appHostPath, "exe");

            var resolved = CurrentExecutableLocator.Resolve(
                processPath: @"C:\Program Files\dotnet\dotnet.exe",
                baseDirectory: dir,
                assemblyLocation: assemblyPath);

            Assert.Equal(appHostPath, resolved);
        }
        finally
        {
            SafeDelete(dir);
        }
    }

    [Fact]
    public void Resolve_ReturnsNullWhenNoExecutableExists()
    {
        var dir = CreateTempDir();
        try
        {
            var assemblyPath = Path.Combine(dir, "ralph.dll");
            File.WriteAllText(assemblyPath, "dll");

            var resolved = CurrentExecutableLocator.Resolve(
                processPath: Path.Combine(dir, "dotnet.exe"),
                baseDirectory: dir,
                assemblyLocation: assemblyPath);

            Assert.Null(resolved);
        }
        finally
        {
            SafeDelete(dir);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphExecutableLocatorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SafeDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { }
    }
}
