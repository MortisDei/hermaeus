using System.Text.RegularExpressions;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class RedactionService : IRedactionService
{
    private static readonly Regex ApiKeyRegex = new(
        @"(?i)(sk-[a-z0-9_\-]{12,}|api[_-]?key[=:]\s*)[a-z0-9_\-\.]{8,}|bearer\s+[a-z0-9_\-\.]{12,}",
        RegexOptions.Compiled);

    public string Redact(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var redacted = ApiKeyRegex.Replace(value, match =>
            match.Value.StartsWith("api", StringComparison.OrdinalIgnoreCase)
                ? "api_key=[redacted]"
                : "[redacted-secret]");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            redacted = redacted.Replace(home, "~");

        return redacted;
    }
}
