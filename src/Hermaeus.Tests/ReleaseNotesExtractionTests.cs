using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r22 3.3: exercises scripts/release-notes.sh (the .ps1 twin follows the same
/// contract and is checked by eye; duplicating every case through pwsh as well
/// would just be the same assertions twice). Runs against fixture changelog
/// content, never the repo's real CHANGELOG.md, so archiving old versions out
/// of that file can never break this test. Skips gracefully (does nothing,
/// still counts as passed) on a host with no bash on PATH or at the usual
/// Git-for-Windows install locations, since CI hosts differ from dev boxes.
/// </summary>
internal static class ReleaseNotesExtractionTests
{
    private const string Fixture =
        """
        # Changelog

        Intro text that is not part of any version section.

        ## [2.0.0] - 2026-08-01

        Latest section body line one.
        Latest section body line two.

        ## [1.5.0] - 2026-07-01

        Middle section body.

        ## [1.0.0] - 2026-06-01

        Oldest section body.
        """;

    /// <summary>
    /// A bash that can actually run this repository's scripts.
    ///
    /// r29: probing `bash --version` alone is not enough on Windows. A machine
    /// with WSL enabled has %LOCALAPPDATA%\Microsoft\WindowsApps\bash.exe ahead
    /// of Git-for-Windows on PATH; it answers --version with exit 0 and then
    /// cannot see a Windows-path script at all, so every run of the script
    /// exited 127. The two cases that assert exit 0 failed, and the three that
    /// assert a NON-zero exit passed for entirely the wrong reason. So the
    /// candidate is now probed against the real script path, and the explicit
    /// Git-for-Windows locations are tried before the bare name.
    /// </summary>
    private static string? FindBash()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ?
            [
                @"C:\Program Files\Git\bin\bash.exe",
                @"C:\Program Files (x86)\Git\bin\bash.exe",
                "bash"
            ]
            : ["bash"];

        var script = ScriptPath();

        foreach (var candidate in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo(candidate)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                psi.ArgumentList.Add("-c");
                // Both halves matter: a shell that cannot see the script, and a
                // shell with no awk, would each leave the script exiting 127.
                psi.ArgumentList.Add($"test -f '{script}' && command -v awk >/dev/null");
                using var probe = Process.Start(psi);
                if (probe is null)
                    continue;
                probe.WaitForExit(5000);
                if (probe.ExitCode == 0)
                    return candidate;
            }
            catch
            {
                // Not usable at this candidate; try the next one.
            }
        }

        return null;
    }

    private static async Task<(int ExitCode, string StdOut)> RunAsync(string bash, string scriptPath, string version, string changelogPath)
    {
        var psi = new ProcessStartInfo(bash)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add(version);
        psi.ArgumentList.Add(changelogPath);

        using var process = Process.Start(psi)!;
        var stdOut = await process.StandardOutput.ReadToEndAsync();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdOut);
    }

    private static string ScriptPath()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine(root, "scripts", "release-notes.sh").Replace('\\', '/');
    }

    public static async Task ReleaseNotesExtractsAnExistingMiddleSection()
    {
        var bash = FindBash();
        if (bash is null) return;

        using var temp = new TempDir();
        var changelog = temp.PathFor("CHANGELOG.md");
        File.WriteAllText(changelog, Fixture);

        var (exitCode, stdOut) = await RunAsync(bash, ScriptPath(), "1.5.0", changelog);

        Equal(0, exitCode, "an existing section should extract cleanly");
        Equal("Middle section body.", stdOut.Trim(), "extracted body should match the fixture section exactly");
    }

    public static async Task ReleaseNotesFailsClearlyForAMissingSection()
    {
        var bash = FindBash();
        if (bash is null) return;

        using var temp = new TempDir();
        var changelog = temp.PathFor("CHANGELOG.md");
        File.WriteAllText(changelog, Fixture);

        var (exitCode, _) = await RunAsync(bash, ScriptPath(), "9.9.9", changelog);

        NotEqual(0, exitCode, "a version with no changelog section should fail, not print an empty release");
    }

    public static async Task ReleaseNotesExtractsTheLatestSectionAtTheTopOfTheFile()
    {
        var bash = FindBash();
        if (bash is null) return;

        using var temp = new TempDir();
        var changelog = temp.PathFor("CHANGELOG.md");
        File.WriteAllText(changelog, Fixture);

        var (exitCode, stdOut) = await RunAsync(bash, ScriptPath(), "2.0.0", changelog);

        Equal(0, exitCode, "the newest section (first in the file) should extract cleanly");
        var normalized = stdOut.Replace("\r\n", "\n").Trim('\n');
        Equal("Latest section body line one.\nLatest section body line two.", normalized,
            "extracted body should contain both lines of the newest section, in order");
    }

    public static async Task ReleaseNotesFailsForAVersionArchivedAwayOutOfTheChangelog()
    {
        var bash = FindBash();
        if (bash is null) return;

        using var temp = new TempDir();
        var changelog = temp.PathFor("CHANGELOG.md");
        // Simulates the real FIFO rotation: an old version's section has
        // already been moved to docs/changelog-archive.md and no longer
        // appears in CHANGELOG.md, so a release for it must still fail.
        File.WriteAllText(changelog, Fixture);

        var (exitCode, _) = await RunAsync(bash, ScriptPath(), "0.5.0", changelog);

        NotEqual(0, exitCode, "a version rotated out to the archive file must not silently produce empty release notes");
    }
}
