namespace Ralph.UI.Abstractions;

public interface IUserInteraction
{
    void WriteInfo(string message);
    void WriteWarn(string message);
    void WriteError(string message);
    void WriteVerbose(string message);
    bool Confirm(string message);
    string? Choose(string title, IReadOnlyList<string> options);
    void ShowProgress(string message, int? percent = null);
}
