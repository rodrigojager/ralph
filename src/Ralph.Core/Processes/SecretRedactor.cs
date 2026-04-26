using System.Text.RegularExpressions;

namespace Ralph.Core.Processes;

public static class SecretRedactor
{
    private static readonly Regex AssignmentRegex = new(
        @"(?i)\b([A-Z0-9_]*(?:KEY|TOKEN|SECRET|PASSWORD|PASS|AUTH)[A-Z0-9_]*)\s*=\s*([^\s;]+)",
        RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return AssignmentRegex.Replace(value, "$1=[redacted]");
    }
}
