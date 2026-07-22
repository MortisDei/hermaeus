using System.Reflection;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r11 1.7: ValidatePythonForXttsAsync's script reported a Python version
/// but nothing ever compared it to MinSupportedXttsPython/MaxSupportedXttsPythonExclusive,
/// so the "Version detection" check always passed regardless of the actual
/// interpreter. These tests call the private
/// ValidatePythonForXttsAsync(pythonPath, prefixArgs, ...) overload via
/// reflection against a fake "python" batch stub (Windows-only, since
/// Process.Start resolves .bat through cmd.exe there) that always reports a
/// fixed version, so no real Python installation is required.
/// </summary>
public sealed class LocalAiSetupXttsPythonGateTests
{
    private const string PassingChecks = """
        HERMAEUS_CHECK=Execute=PASS
        HERMAEUS_CHECK=Version detection=PASS
        HERMAEUS_CHECK=Import encodings=PASS
        HERMAEUS_CHECK=Import venv=PASS
        HERMAEUS_CHECK=Valid sys.prefix=PASS
        HERMAEUS_CHECK=Create test venv=PASS
        """;

    private static async Task<(bool Result, List<string> Log)> InvokeValidateAsync(string pythonPath, string workingDirectory)
    {
        var method = typeof(LocalAiSetupService).GetMethod(
            "ValidatePythonForXttsAsync",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(string), typeof(IReadOnlyList<string>), typeof(string), typeof(IProgress<string>), typeof(CancellationToken)])
            ?? throw new InvalidOperationException("ValidatePythonForXttsAsync(pythonPath, prefixArgs, ...) overload not found.");

        var log = new List<string>();
        var progress = new Progress<string>(log.Add);
        var task = (Task<bool>)method.Invoke(null, [pythonPath, Array.Empty<string>(), workingDirectory, progress, CancellationToken.None])!;
        var result = await task;
        return (result, log);
    }

    private static string WriteStub(TempDir temp, string version)
    {
        var path = temp.PathFor($"py-{Guid.NewGuid():N}.bat");
        var lines = new[] { "@echo off", $"echo HERMAEUS_VERSION={version}" }
            .Concat(PassingChecks.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(l => $"echo {l}"));
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public async Task ValidatePythonForXttsAsync_rejects_a_version_outside_the_supported_range()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDir();
        var workDir = temp.PathFor("work");
        Directory.CreateDirectory(workDir);
        var stub = WriteStub(temp, "3.13");

        var (result, log) = await InvokeValidateAsync(stub, workDir);

        Assert.True(!result, $"a 3.13 interpreter must fail the XTTS 3.9-3.11 version gate. Log:\n{string.Join('\n', log)}");
    }

    [Fact]
    public async Task ValidatePythonForXttsAsync_accepts_a_version_inside_the_supported_range()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDir();
        var workDir = temp.PathFor("work");
        Directory.CreateDirectory(workDir);
        var stub = WriteStub(temp, "3.11");

        var (result, log) = await InvokeValidateAsync(stub, workDir);

        Assert.True(result, $"a 3.11 interpreter should pass the XTTS 3.9-3.11 version gate. Log:\n{string.Join('\n', log)}");
    }
}
