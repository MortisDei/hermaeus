using System.Diagnostics;
using Aether.Core.Models;

namespace Aether.Services.ProcessManagement;

/// <summary>Result of checking whether a configured server's port is held by a leftover process (r9 02-server-lifecycle.md 2.3).</summary>
public sealed record OrphanServerInfo(string ServerId, string ServerName, int Port, int Pid, string ProcessName, bool IsOwnBinary);

public sealed record OrphanStopResult(bool Success, string Message)
{
    public static OrphanStopResult Ok() => new(true, string.Empty);
    public static OrphanStopResult Refused(string message) => new(false, message);
}

/// <summary>
/// At startup and on Services view refresh, for each configured managed
/// server that is not Running, checks whether its port is held by a process
/// whose executable matches the server's configured binary exactly. Only an
/// exact-path match is a stoppable "own orphan"; anything else is reported
/// but never offered a Stop button (security-posture: the app must never
/// terminate a process it cannot positively identify as its own). Process
/// enumeration lives behind <see cref="IPortOwnerLookup"/> so this class is
/// unit-testable with a fake, no real second process required.
/// </summary>
public sealed class OrphanServerDetector
{
    private readonly IPortOwnerLookup _lookup;

    public OrphanServerDetector(IPortOwnerLookup? lookup = null)
    {
        _lookup = lookup ?? PortOwnerLookup.Default;
    }

    /// <summary>Null when the port is free. Otherwise names the occupying process and whether it is this server's own binary.</summary>
    public OrphanServerInfo? Detect(ServerConfig config)
    {
        var owner = _lookup.FindOwner(config.Port);
        if (owner is null)
            return null;

        return new OrphanServerInfo(
            config.Id, config.Name, config.Port, owner.Pid, owner.ProcessName,
            IsSameExecutable(owner.ExecutablePath, ResolveConfiguredExecutable(config.ExecutablePath)));
    }

    /// <summary>
    /// Re-verifies the PID still owns the port and still runs the configured
    /// executable immediately before killing it (PID reuse guard), refusing
    /// on any mismatch. Only ever called from an explicit user click.
    /// </summary>
    public OrphanStopResult TryStop(ServerConfig config, int expectedPid)
    {
        var owner = _lookup.FindOwner(config.Port);
        if (owner is null || owner.Pid != expectedPid)
            return OrphanStopResult.Refused("The process on this port has changed since it was detected; refusing to stop it.");

        if (!IsSameExecutable(owner.ExecutablePath, ResolveConfiguredExecutable(config.ExecutablePath)))
            return OrphanStopResult.Refused("The process no longer matches this server's configured executable; refusing to stop it.");

        try
        {
            using var process = Process.GetProcessById(expectedPid);
            process.Kill(entireProcessTree: true);
            return OrphanStopResult.Ok();
        }
        catch (Exception ex)
        {
            return OrphanStopResult.Refused($"Failed to stop process {expectedPid}: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves a configured value (bare name, directory, or full path)
    /// against the same rules ServerProcessManager launches with, so a bare
    /// "llama-server" or a directory still matches the running process's
    /// resolved .exe (r11 1.3). Falls back to the raw value on failure; this
    /// only weakens a match, it never widens one.
    /// </summary>
    private static string? ResolveConfiguredExecutable(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        var resolution = ExecutableResolver.Resolve(configuredPath.Trim(), "llama-server");
        return resolution.Success ? resolution.Path : configuredPath;
    }

    internal static bool IsSameExecutable(string? actual, string? configured)
    {
        if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(configured))
            return false;

        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(actual), Path.GetFullPath(configured), comparison);
        }
        catch
        {
            return false;
        }
    }
}
