using Ralph.UI.Abstractions;

namespace Ralph.UI.Console;

public sealed class NoopInteraction : IUserInteraction
{
    public void WriteInfo(string message) { }
    public void WriteWarn(string message) { }
    public void WriteError(string message) { }
    public void WriteVerbose(string message) { }
    public bool Confirm(string message) => false;
    public string? Choose(string title, IReadOnlyList<string> options) => null;
    public void ShowProgress(string message, int? percent = null) { }
}
