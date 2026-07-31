using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r11 1.7: PythonHealthValidator.IsRequiredVersion used to accept any minor
/// &gt;= required with no ceiling, so Doctor's "Python 3.11 for XTTS v2" check
/// passed on 3.13, which coqui TTS does not support. These tests drive
/// ValidateAsync against a fake "python" batch stub (Windows-only, since
/// Process.Start natively resolves .bat through cmd.exe there) that always
/// reports a fixed version, so no real Python installation is required.
/// </summary>
public sealed class PythonHealthValidatorTests
{
    private static readonly string HealthScriptTemplate = """
        @echo off
        echo {{"version": "{0}", "issues": [], "base_prefix": "C:/Fake/Python", "executable": "C:/Fake/Python/python.exe"}}
        """;

    private sealed class FakeVoiceProvider(
        (int Major, int Minor)? required,
        (int Major, int Minor)? maxExclusive = null) : IVoiceProvider
    {
        public VoiceProvider Id => VoiceProvider.XttsV2;
        public string DisplayName => "Fake";
        public VoiceCapability Capabilities => VoiceCapability.TextToSpeech;
        public (int Major, int Minor)? RequiredPythonVersion => required;
        public (int Major, int Minor)? MaxExclusivePythonVersion => maxExclusive;
        public bool IsInstalled => true;
        public VoiceProviderDetection Detect() => new(true, "ok", "ok");
        public VoiceInstallPlan InstallPlan() => new("none", [], "low");
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default) =>
            Task.FromResult(new VoiceHealth(VoiceHealthStatus.Healthy, "ok", "ok"));
        public Task<IReadOnlyList<VoiceDefinition>> ListVoicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VoiceDefinition>>([]);
        public Task<VoiceSynthesisResult> GenerateSpeechAsync(VoiceSynthesisRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static string WriteFakePython(TempDir temp, string version)
    {
        var path = temp.PathFor($"fake-python-{Guid.NewGuid():N}.bat");
        File.WriteAllText(path, string.Format(HealthScriptTemplate, version));
        return path;
    }

    [WindowsOnlyFact]
    public async Task ValidateAsync_rejects_a_version_at_or_above_the_providers_max_exclusive()
    {
        using var temp = new TempDir();
        var fakePython = WriteFakePython(temp, "3.13.5");
        var xtts = new FakeVoiceProvider((3, 9), (3, 12));
        var validator = PythonHealthValidator.ForProvider(xtts);

        var report = await validator.ValidateAsync(fakePython);

        Assert.False(report.IsHealthy, "Python 3.13 should fail the XTTS 3.9-3.11 range gate");
        Assert.Contains(report.Issues, i => i.Code == "version");
    }

    [WindowsOnlyFact]
    public async Task ValidateAsync_accepts_a_version_inside_the_providers_range()
    {
        using var temp = new TempDir();
        var fakePython = WriteFakePython(temp, "3.11.4");
        var xtts = new FakeVoiceProvider((3, 9), (3, 12));
        var validator = PythonHealthValidator.ForProvider(xtts);

        var report = await validator.ValidateAsync(fakePython);

        Assert.True(report.IsHealthy, report.Detail);
    }

    /// <summary>Kokoro has a 3.12 floor and no ceiling; a newer interpreter must still be accepted.</summary>
    [WindowsOnlyFact]
    public async Task ValidateAsync_accepts_any_newer_minor_when_the_provider_names_no_ceiling()
    {
        using var temp = new TempDir();
        var fakePython = WriteFakePython(temp, "3.15.0");
        var kokoro = new FakeVoiceProvider((3, 12), maxExclusive: null);
        var validator = PythonHealthValidator.ForProvider(kokoro);

        var report = await validator.ValidateAsync(fakePython);

        Assert.True(report.IsHealthy, report.Detail);
    }
}
