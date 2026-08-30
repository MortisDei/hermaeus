using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

public static partial class RuntimeIdentityFactory
{
    public static RuntimeIdentityV2 Unknown(string kind) => new(
        kind, string.Empty, null, null, string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, IdentityCompleteness.Incomplete);

    public static async Task<RuntimeIdentityV2> CreateRuntimeIdentityAsync(
        string configuredPath,
        string? versionOrHelpText,
        CancellationToken ct = default)
    {
        var resolved = ExecutableResolver.Resolve(configuredPath, "llama-server").Path;
        if (string.IsNullOrWhiteSpace(resolved))
            return Unknown("llama.cpp");

        var file = new FileInfo(resolved);
        if (!file.Exists)
            return Unknown("llama.cpp");

        string hash = string.Empty;
        try
        {
            await using var stream = new FileStream(
                file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        var parsed = ParseVersion(versionOrHelpText);
        return new RuntimeIdentityV2(
            "llama.cpp", hash, file.Length, file.LastWriteTimeUtc,
            parsed.Version, parsed.Build, parsed.Compiler, parsed.Backend,
            string.Empty, string.IsNullOrWhiteSpace(hash) ? IdentityCompleteness.Incomplete : IdentityCompleteness.Complete);
    }

    public static ModelIdentityV2 CreateModelIdentity(string modelPath, GgufModelInfo? gguf, string? verifiedSha256 = null, string? manifestIdentity = null, string? companionIdentity = null)
    {
        var file = new FileInfo(modelPath);
        var hasHash = !string.IsNullOrWhiteSpace(verifiedSha256);
        var hasManifest = !string.IsNullOrWhiteSpace(manifestIdentity);
        var strength = hasHash
            ? ModelIdentityStrength.VerifiedHash
            : hasManifest
                ? ModelIdentityStrength.Manifest
                : file.Exists
                    ? ModelIdentityStrength.FileMetadataFallback
                    : ModelIdentityStrength.Unknown;
        return new ModelIdentityV2(
            manifestIdentity?.Trim() ?? string.Empty,
            verifiedSha256?.Trim().ToLowerInvariant() ?? string.Empty,
            file.Exists ? file.Length : null,
            file.Exists ? file.LastWriteTimeUtc : null,
            gguf?.Architecture ?? string.Empty,
            gguf?.Quantization ?? string.Empty,
            companionIdentity?.Trim() ?? string.Empty,
            strength,
            hasHash || hasManifest ? IdentityCompleteness.Complete : IdentityCompleteness.Incomplete);
    }

    public static (string Version, string Build, string Compiler, string Backend) ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (string.Empty, string.Empty, string.Empty, string.Empty);

        var build = BuildPattern().Match(text).Groups[1].Value;
        var version = VersionPattern().Match(text).Groups[1].Value;
        var compiler = CompilerPattern().Match(text).Groups[1].Value.Trim();
        var backend = BackendPattern().Match(text).Groups[1].Value.Trim();
        return (version, build, compiler, backend);
    }

    [GeneratedRegex(@"\bbuild\s*[:=]?\s*(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BuildPattern();

    [GeneratedRegex(@"\b(?:version|tag)\s*[:=]?\s*([^\s,;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"\bcompiler\s*[:=]\s*([^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CompilerPattern();

    [GeneratedRegex(@"\bbackend\s*[:=]\s*([^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BackendPattern();
}
