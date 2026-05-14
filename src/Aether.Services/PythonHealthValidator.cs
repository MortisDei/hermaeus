using System.Diagnostics;
using System.Text.Json;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class PythonHealthValidator
{
    private const int DefaultRequiredMajor = 3;
    private const int DefaultRequiredMinor = 11;

    private readonly int _requiredMajor;
    private readonly int _requiredMinor;

    public PythonHealthValidator(int requiredMajor = DefaultRequiredMajor, int requiredMinor = DefaultRequiredMinor)
    {
        _requiredMajor = requiredMajor;
        _requiredMinor = requiredMinor;
    }

    public static PythonHealthValidator ForProvider(IVoiceProvider provider)
    {
        var (major, minor) = provider.RequiredPythonVersion;
        return major > 0 ? new PythonHealthValidator(major, minor) : new PythonHealthValidator();
    }

    public async Task<PythonHealthReport> ValidateAsync(string pythonPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pythonPath))
        {
            return new PythonHealthReport(false, string.Empty, [
                new PythonHealthIssue("path-missing", "Python path is empty.")
            ], "Python path missing", "Configure a Python 3.11 interpreter.", "Python path is empty.");
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
        return version.StartsWith($"{_requiredMajor}.{_requiredMinor}", StringComparison.Ordinal);
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
