using System.Text.RegularExpressions;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class RedactionService : IRedactionService
{
    private static readonly Regex ApiKeyRegex = new(
        @"(?i)AKIA[0-9A-Z]{16}|ASIA[0-9A-Z]{16}|sk-[a-z0-9_\-]{12,}|gh[pousr]_[a-z0-9_]{20,}|anthropic[_-]?key[=:]\s*[a-z0-9_\-\.]{8,}|bearer\s+[a-z0-9_\-\.]{12,}|((?:api[_-]?key|password|azure[_-]?key)[=:]\s*)[a-z0-9_\-\.]{8,}|([?&](?:key|token|api_key|password)=)[^&\s]+",
        RegexOptions.Compiled);

    public string Redact(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var redacted = ApiKeyRegex.Replace(value, match =>
            match.Value.Contains('=', StringComparison.Ordinal)
             && !match.Value.StartsWith("?", StringComparison.Ordinal)
             && !match.Value.StartsWith("&", StringComparison.Ordinal)
                ? $"{match.Value.Split('=')[0]}=[redacted]"
                : match.Value.StartsWith("?key=", StringComparison.OrdinalIgnoreCase)
                  || match.Value.StartsWith("&key=", StringComparison.OrdinalIgnoreCase)
                  || match.Value.StartsWith("?token=", StringComparison.OrdinalIgnoreCase)
                  || match.Value.StartsWith("&token=", StringComparison.OrdinalIgnoreCase)
                  || match.Value.StartsWith("?api_key=", StringComparison.OrdinalIgnoreCase)
                  || match.Value.StartsWith("&api_key=", StringComparison.OrdinalIgnoreCase)
                  || match.Value.StartsWith("?password=", StringComparison.OrdinalIgnoreCase)
                  || match.Value.StartsWith("&password=", StringComparison.OrdinalIgnoreCase)
                    ? $"{match.Value[0]}{match.Value[1..].Split('=')[0]}=[redacted]"
                : "[redacted-secret]");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            redacted = redacted.Replace(home, "~");

        return redacted;
    }
}
