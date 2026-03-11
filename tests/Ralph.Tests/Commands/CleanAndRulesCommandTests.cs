using Ralph.Cli.Commands;
using Ralph.Core.Localization;
using Ralph.Persistence.Workspace;

namespace Ralph.Tests.Commands;

public class CleanAndRulesCommandTests
{
    [Fact]
    public void Clean_WithoutForce_ReturnsErrorAndKeepsWorkspace()
    {
        var dir = CreateTempDir();
        try
        {
            var workspace = new WorkspaceInitializer();
            workspace.Initialize(dir);
            var command = new CleanCommand(workspace);

            var exit = command.Execute(dir, force: false, StringCatalog.Default());

            Assert.Equal(1, exit);
            Assert.True(Directory.Exists(workspace.GetRalphDir(dir)));
        }
        finally
        {
            SafeDelete(dir);
        }
    }

    [Fact]
    public void Rules_ClearWithoutForce_ReturnsErrorAndKeepsRules()
    {
        var dir = CreateTempDir();
        try
        {
            var workspace = new WorkspaceInitializer();
            workspace.Initialize(dir);
            var guardrailsPath = workspace.GetGuardrailsPath(dir);
            File.WriteAllText(guardrailsPath, "# Guardrails\n\n- keep me\n");
            var command = new RulesCommand(workspace);

            var exit = command.Execute(dir, "clear", null, force: false, StringCatalog.Default());

            Assert.Equal(1, exit);
            Assert.Contains("keep me", File.ReadAllText(guardrailsPath), StringComparison.Ordinal);
        }
        finally
        {
            SafeDelete(dir);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphCommandTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SafeDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { }
    }
}
