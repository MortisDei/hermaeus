using System.Net;
using System.Security.Cryptography;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services.ProcessManagement;

namespace Aether.Services;

public sealed class TrustService : ITrustService
{
    private static readonly string[] UnsafeHostFlags = ["--host", "-host", "--listen-host"];

    public async Task<TrustScanReport> ScanAsync(AppSettings settings, CancellationToken ct = default)
    {
        var scannedAt = DateTime.UtcNow;
        var items = new List<TrustItem>();

        foreach (var server in settings.ManagedServers)
        {
            items.Add(await InspectPathAsync("Managed server", $"{server.Name} executable", server.ExecutablePath, settings.DataManagement.LocalAiAssetsRoot, PathTargetKind.File, scannedAt, ct));
            items.Add(await InspectPathAsync("Managed server", $"{server.Name} model", server.ModelPath, settings.DataManagement.LocalAiAssetsRoot, PathTargetKind.File, scannedAt, ct));
            items.AddRange(AnalyzeServerExtraArgs(server, scannedAt));
        }

        foreach (var profile in settings.RuntimeProfiles)
            items.Add(InspectEndpoint(profile, scannedAt));

        items.Add(await InspectPathAsync("XTTS", "XTTS Python", settings.Tts.PythonPath, settings.DataManagement.LocalAiAssetsRoot, PathTargetKind.File, scannedAt, ct));
        items.Add(await InspectPathAsync("XTTS", "XTTS API script", settings.Tts.ScriptPath, settings.DataManagement.LocalAiAssetsRoot, PathTargetKind.File, scannedAt, ct));
        items.Add(await InspectPathAsync("XTTS", "XTTS model directory", settings.Tts.ModelDirectory, settings.DataManagement.LocalAiAssetsRoot, PathTargetKind.Directory, scannedAt, ct));
        items.Add(await InspectPathAsync("XTTS", "XTTS voices directory", settings.Tts.VoiceDirectory, settings.DataManagement.LocalAiAssetsRoot, PathTargetKind.Directory, scannedAt, ct));
        items.Add(await InspectPathAsync("XTTS", "XTTS output directory", settings.Tts.OutputDirectory, settings.DataManagement.LocalAiAssetsRoot, PathTargetKind.Directory, scannedAt, ct));

        var warningCount = items.Count(i => i.Status == TrustItemStatus.Warning);
        var missingCount = items.Count(i => i.Status == TrustItemStatus.Missing);
        var summary = warningCount == 0 && missingCount == 0
            ? "Trust scan found no warnings."
            : $"Trust scan found {warningCount} warning(s) and {missingCount} missing item(s).";
        return new TrustScanReport(items, summary, scannedAt);
    }

    public IReadOnlyList<TrustItem> AnalyzeServerExtraArgs(ServerConfig server, DateTime scannedAt)
    {
        var args = ExtraArgsParser.Split(server.ExtraArgs).ToList();
        if (args.Count == 0)
            return [];

        var items = new List<TrustItem>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var splitHost = SplitHostFlag(arg);
            if (UnsafeHostFlags.Contains(arg, StringComparer.OrdinalIgnoreCase) || splitHost is not null)
            {
                var value = splitHost ?? (i + 1 < args.Count ? args[i + 1] : string.Empty);
                if (IsNonLoopbackHost(value))
                {
                    items.Add(new TrustItem(
                        "Network exposure",
                        $"{server.Name} extra args",
                        $"{arg} {value}".Trim(),
                        $"{arg} {value}".Trim(),
                        TrustItemStatus.Warning,
                        TrustRiskLevel.High,
                        null,
                        string.Empty,
                        "This host value can expose llama-server beyond this machine. Keep 127.0.0.1 unless you intend network access.",
                        scannedAt));
                }
            }

            if (string.Equals(arg, "--listen", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new TrustItem(
                    "Network exposure",
                    $"{server.Name} extra args",
                    arg,
                    arg,
                    TrustItemStatus.Warning,
                    TrustRiskLevel.High,
                    null,
                    string.Empty,
                    "The listen flag may expose llama-server beyond localhost. Use only when you intend network access.",
                    scannedAt));
            }
        }

        return items;
    }

    private static string? SplitHostFlag(string arg)
    {
        foreach (var flag in UnsafeHostFlags)
        {
            var prefix = flag + "=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arg[prefix.Length..];
        }

        return null;
    }

    private static async Task<TrustItem> InspectPathAsync(
        string category,
        string label,
        string target,
        string aiRoot,
        PathTargetKind kind,
        DateTime scannedAt,
        CancellationToken ct)
    {
        target = target.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            return new TrustItem(category, label, string.Empty, string.Empty, TrustItemStatus.Missing, TrustRiskLevel.High,
                string.IsNullOrWhiteSpace(aiRoot) ? null : false, string.Empty, "Choose a path before using this item.", scannedAt);
        }

        var resolved = ResolvePathTarget(target, kind);
        var exists = kind == PathTargetKind.Directory
            ? Directory.Exists(resolved)
            : File.Exists(resolved);
        if (!exists)
        {
            return new TrustItem(category, label, target, resolved, TrustItemStatus.Missing, TrustRiskLevel.High,
                ScopeFor(resolved, aiRoot), string.Empty, "The configured path was not found. Choose an existing local path.", scannedAt);
        }

        var inside = ScopeFor(resolved, aiRoot);
        var hash = kind == PathTargetKind.File ? await HashFileAsync(resolved, ct) : string.Empty;
        if (inside == false)
        {
            return new TrustItem(category, label, target, resolved, TrustItemStatus.Warning, TrustRiskLevel.Medium,
                inside, hash, "This path is outside the configured AI assets root. Verify that you trust it before running.", scannedAt);
        }

        return new TrustItem(category, label, target, resolved, TrustItemStatus.Ready, TrustRiskLevel.Low,
            inside, hash, "Path exists and is ready.", scannedAt);
    }

    private static TrustItem InspectEndpoint(RuntimeProfile profile, DateTime scannedAt)
    {
        if (!Uri.TryCreate(profile.BaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return new TrustItem("Runtime endpoint", profile.Name, profile.BaseUrl, profile.BaseUrl,
                TrustItemStatus.Warning, TrustRiskLevel.Medium, null, string.Empty,
                "Endpoint URL could not be parsed.", scannedAt);
        }

        var loopback = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip);
        if (!loopback)
        {
            return new TrustItem("Runtime endpoint", profile.Name, profile.BaseUrl, uri.ToString(),
                TrustItemStatus.Warning, TrustRiskLevel.Medium, null, string.Empty,
                "Remote or wildcard endpoint. Use only if you intend to send prompts outside this machine.", scannedAt);
        }

        return new TrustItem("Runtime endpoint", profile.Name, profile.BaseUrl, uri.ToString(),
            TrustItemStatus.Info, TrustRiskLevel.Low, null, string.Empty,
            "Endpoint is local loopback.", scannedAt);
    }

    private static string ResolvePathTarget(string target, PathTargetKind kind)
    {
        if (kind == PathTargetKind.File && !LooksLikePath(target) && !Path.IsPathFullyQualified(target))
            return FindOnPath(target) ?? target;

        return Path.GetFullPath(target);
    }

    private static bool? ScopeFor(string path, string aiRoot)
    {
        if (string.IsNullOrWhiteSpace(aiRoot) || !Directory.Exists(aiRoot))
            return null;

        var rootFull = Path.GetFullPath(aiRoot.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        var pathFull = Path.GetFullPath(path);
        if (Directory.Exists(pathFull))
            pathFull = pathFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return pathFull.StartsWith(rootFull, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsNonLoopbackHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        if (value.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return false;
        if (IPAddress.TryParse(value, out var ip))
            return !IPAddress.IsLoopback(ip);

        return !value.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
               && !value.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindOnPath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir, executableName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static bool LooksLikePath(string value) =>
        value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar);

    private enum PathTargetKind
    {
        File,
        Directory
    }
}
