using System.Diagnostics;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class PythonHealthValidator
{
    private const int DefaultRequiredMajor = 3;
    private const int DefaultRequiredMinor = 11;

    private readonly int _requiredMajor;
    private readonly int _requiredMinor;
    private readonly int? _maxExclusiveMinor;

    public PythonHealthValidator(int requiredMajor = DefaultRequiredMajor, int requiredMinor = DefaultRequiredMinor, int? maxExclusiveMinor = null)
    {
        _requiredMajor = requiredMajor;
        _requiredMinor = requiredMinor;
        _maxExclusiveMinor = maxExclusiveMinor;
    }

    /// <summary>
    /// r11 1.7: per-provider max so Doctor and the setup wizard's own XTTS
    /// validation agree about the same requirement instead of disagreeing
    /// (setup claimed max-exclusive 3.12; Doctor's IsRequiredVersion accepted
    /// any minor &gt;= required, so 3.13 passed the Doctor check).
    /// </summary>
    public static PythonHealthValidator ForProvider(IVoiceProvider provider)
    {
        var required = provider.RequiredPythonVersion;
        if (required is not { } version)
            return new PythonHealthValidator();

        var maxExclusive = provider.MaxExclusivePythonVersion;
        if (maxExclusive is { } max && max.Major == version.Major)
            return new PythonHealthValidator(version.Major, version.Minor, max.Minor);

        return new PythonHealthValidator(version.Major, version.Minor);
    }

    public async Task<PythonHealthReport> ValidateAsync(string pythonPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pythonPath))
        {
            return new PythonHealthReport(false, string.Empty, [
                new PythonHealthIssue("path-missing", "Python path is empty.")
            ], "Python path missing", $"Configure a Python {_requiredMajor}.{_requiredMinor} interpreter.", "Python path is empty.");
        }

        if (!File.Exists(pythonPath))
        {
            return new PythonHealthReport(false, string.Empty, [
                new PythonHealthIssue("path-missing", $"Python path not found: {pythonPath}")
            ], "Python path missing", "The configured Python path does not exist.", pythonPath);
        }

        var script = """
import json
import os
import sys
import tempfile
issues = []

def add(code, message):
    issues.append({"code": code, "message": message})

version = f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}"
try:
    import encodings  # noqa: F401
except Exception as exc:
    add("encodings", f"cannot import encodings: {exc}")

try:
    import venv as venv_mod  # noqa: F401
except Exception as exc:
    add("venv", f"cannot import venv: {exc}")

if not getattr(sys, "executable", None):
    add("executable", "sys.executable is missing")

base_prefix = getattr(sys, "base_prefix", None) or ""
if not base_prefix:
    add("base_prefix", "sys.base_prefix is missing")
elif not os.path.exists(base_prefix):
    add("base_prefix", f"base_prefix path missing: {base_prefix}")

if getattr(sys, "prefix", "") == "/install":
    add("non_relocatable", "sys.prefix is /install")

try:
    import venv
    tmp = tempfile.mkdtemp()
    venv.EnvBuilder(with_pip=False).create(tmp)
except Exception as exc:
    add("venv_create", f"test venv creation failed: {exc}")

print(json.dumps({"version": version, "issues": issues, "base_prefix": base_prefix, "executable": getattr(sys, "executable", "")}))
""";

        var (success, output) = await RunPythonAsync(pythonPath, script, ct);
        if (!success)
        {
            return new PythonHealthReport(false, string.Empty, [
                new PythonHealthIssue("python-failed", output)
            ], "Python health check failed", output, output);
        }

        return ParseOutput(output, pythonPath);
    }

    private PythonHealthReport ParseOutput(string output, string pythonPath)
    {
        try
        {
            var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            var version = root.GetProperty("version").GetString() ?? string.Empty;
            var issues = new List<PythonHealthIssue>();

            if (root.TryGetProperty("issues", out var issuesElement))
            {
                foreach (var issue in issuesElement.EnumerateArray())
                {
                    var code = issue.GetProperty("code").GetString() ?? "unknown";
                    var message = issue.GetProperty("message").GetString() ?? "unknown";
                    issues.Add(new PythonHealthIssue(code, message));
                }
            }

            if (!IsRequiredVersion(version))
            {
                issues.Add(new PythonHealthIssue("version", $"Expected Python {_requiredMajor}.{_requiredMinor}, got {version}.") );
            }

            var healthy = issues.Count == 0;
            var summary = healthy ? "Python is healthy" : "Python is not healthy";
            var detail = healthy
                ? $"Python {version} at {pythonPath}"
                : string.Join(" ", issues.Select(i => i.Message));
            var diagnostics = $"Python path: {pythonPath}\nVersion: {version}\n{detail}";
            return new PythonHealthReport(healthy, version, issues, summary, detail, diagnostics);
        }
        catch (Exception ex)
        {
            var message = $"Failed to parse python health output: {ex.Message}";
            return new PythonHealthReport(false, string.Empty, [
                new PythonHealthIssue("parse", message)
            ], "Python health check failed", message, output);
        }
    }

    private bool IsRequiredVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        // Accept any patch version and any newer minor version within the same major,
        // up to (but excluding) _maxExclusiveMinor when the provider names one
        // (r11 1.7: this used to accept any minor >= required with no ceiling,
        // so Doctor's "Python 3.11 for XTTS v2" check passed on 3.13, which
        // coqui TTS does not support).
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out var major)) return false;
        if (!int.TryParse(parts[1], out var minor)) return false;
        if (major != _requiredMajor) return false;
        if (minor < _requiredMinor) return false;
        return _maxExclusiveMinor is not { } maxExclusive || minor < maxExclusive;
    }

    private static async Task<(bool Success, string Output)> RunPythonAsync(string pythonPath, string script, CancellationToken ct)
    {
        var lines = new List<string>();
        var psi = new ProcessStartInfo
        {
            FileName = pythonPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lines.Add(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lines.Add(e.Data); };
        if (!process.Start())
            return (false, "Failed to start python process.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);
        return (process.ExitCode == 0, string.Join("\n", lines));
    }
}
