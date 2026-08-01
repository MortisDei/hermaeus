using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r29 doc 04 4.3: a fact that runs only on Windows, and reports Skipped rather
/// than Passed elsewhere.
///
/// Sixteen tests used to open with <c>if (!OperatingSystem.IsWindows()) return;</c>,
/// so the Linux CI leg recorded a green tick for llama-server installation,
/// Python health validation, job-object assignment and manifest behaviour it
/// never executed, and both legs reported <c>Skipped: 0</c>. A suite that
/// reports Passed for work it did not do is not telling the truth about itself.
///
/// Built on xunit 2.9.2's own <see cref="FactAttribute.Skip"/>, which is
/// evaluated at discovery, so no package is needed.
///
/// Use this when the behaviour under test is genuinely Windows-only (a Windows
/// API, an .exe, a job object). Do NOT use it to dodge a cross-platform
/// difference that the test could assert on both platforms; branch inside the
/// test instead, as BackupMigrationTests and ServiceTests already do.
/// </summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only behaviour.";
    }
}
