using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class LocalAiSetupService : ILocalAiSetupService
{
    private static readonly string[] XttsPackages = ["TTS", "fastapi", "uvicorn", "soundfile"];
    private static readonly Version MinSupportedXttsPython = new(3, 9);
    private static readonly Version MaxSupportedXttsPythonExclusive = new(3, 12);

    public Task<LocalAiReadinessReport> ScanAsync(AppSettings settings, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var root = settings.LocalAiAssetsRoot.Trim();
        var layout = LocalAiAssetLocator.Detect(root);
        var items = new List<LocalAiReadinessItem>();
        var actions = new List<LocalAiSetupAction>();

        if (string.IsNullOrWhiteSpace(layout.Root) || !Directory.Exists(layout.Root))
        {
            items.Add(new("root", "AI folder", LocalAiReadinessStatus.Missing,
                "Choose an existing folder before scanning.", "Aether only scans the folder you select.", true));
            return Task.FromResult(new LocalAiReadinessReport(layout.Root, items, actions,
                "Choose an existing local AI folder first.", string.Empty));
        }

        var defaultVenv = Path.Combine(layout.Root, "venv");
        var defaultScript = Path.Combine(layout.Root, "TTS", "xtts_api_server.py");
        var defaultVoices = Path.Combine(layout.Root, "TTS", "voices");
        var defaultOutput = Path.Combine(layout.Root, "TTS", "output");
        var venvPython = string.IsNullOrWhiteSpace(layout.TtsPythonPath) ? PythonPathForVenv(defaultVenv) : layout.TtsPythonPath;

        AddItem(items, "models", "GGUF models", layout.ModelsDirectory,
            "Found model folder.", "Missing Models folder with .gguf files.", true);
        AddItem(items, "venv", "Python venv", File.Exists(venvPython) ? venvPython : string.Empty,
            "Found Python in a local venv.", "No local venv Python was found.", true);
        AddItem(items, "xtts-model", "XTTS v2 model", layout.TtsModelDirectory,
            "Found XTTS v2 model files.", "XTTS v2 model files were not found.", true);
        AddItem(items, "xtts-script", "XTTS API script", layout.TtsScriptPath,
            "Found xtts_api_server.py.", "Aether can create a local API script.", true);
        AddItem(items, "voices", "Voice samples", layout.TtsVoiceDirectory,
            "Found voice sample folder.", "Voice folder can be created.", false);
        AddItem(items, "output", "Audio output", layout.TtsOutputDirectory,
            "Found XTTS output folder.", "Output folder can be created.", false);
        AddItem(items, "reranker", "RAG reranker", layout.RerankerDirectory,
            "Found reranker model folder.", "Reranker is optional for RAG quality.", false);

        if (!File.Exists(venvPython))
            actions.Add(CreateVenvAction(defaultVenv));

        var installPythonReady = File.Exists(venvPython);
        var installPython = installPythonReady
            ? venvPython
            : PythonPathForVenv(defaultVenv);
        actions.Add(InstallXttsAction(installPython, installPythonReady));

        if (string.IsNullOrWhiteSpace(layout.TtsScriptPath))
            actions.Add(CreateScriptAction(defaultScript));

        if (string.IsNullOrWhiteSpace(layout.TtsVoiceDirectory))
            actions.Add(CreateDirectoryAction("voices", "Create voice sample folder", defaultVoices));
        if (string.IsNullOrWhiteSpace(layout.TtsOutputDirectory))
            actions.Add(CreateDirectoryAction("output", "Create XTTS output folder", defaultOutput));

        var missingRequired = items.Count(i => i.Required && i.Status != LocalAiReadinessStatus.Found);
        var summary = missingRequired == 0
            ? $"Local AI setup is ready under {layout.Root}."
            : $"{missingRequired} required item(s) need attention under {layout.Root}.";
        var commands = string.Join(Environment.NewLine, actions.Select(a => a.CommandPreviewText));

        return Task.FromResult(new LocalAiReadinessReport(layout.Root, items, actions, summary, commands));
    }

    public async Task<LocalAiSetupResult> RunActionAsync(
        LocalAiSetupAction action,
        AppSettings settings,
        bool allowOverwrite = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report($"Action: {action.Title}");
        progress?.Report($"Target: {action.TargetPath}");
        progress?.Report($"Risk: {action.RiskLabel}");

        return action.Kind switch
        {
            LocalAiSetupActionKind.CreateVenv => await CreateVenvAsync(action.TargetPath, progress, ct),
            LocalAiSetupActionKind.InstallXttsDependencies => await InstallXttsAsync(action.TargetPath, progress, ct),
            LocalAiSetupActionKind.CreateXttsApiScript => CreateXttsApiScript(action.TargetPath, settings, allowOverwrite, progress),
            LocalAiSetupActionKind.CreateDirectory => CreateSupportDirectory(action.TargetPath, progress),
            _ => new LocalAiSetupResult(false, $"Unsupported setup action: {action.Kind}")
        };
    }

    public string BuildXttsApiScript(string? modelDirectory = null, string? outputDirectory = null)
    {
        var modelDefault = string.IsNullOrWhiteSpace(modelDirectory) ? "None" : $"r'''{modelDirectory.Trim()}'''";
        var outputDefault = string.IsNullOrWhiteSpace(outputDirectory) ? "None" : $"r'''{outputDirectory.Trim()}'''";
        return $$"""
#!/usr/bin/env python3
import argparse
import os
import time
import uuid
from pathlib import Path
from typing import Optional

import uvicorn
from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse
from pydantic import BaseModel


DEFAULT_MODEL_DIR = {{modelDefault}}
DEFAULT_OUTPUT_DIR = {{outputDefault}}
app = FastAPI(title="Aether XTTS v2 API")
tts_engine = None
settings = None


class SpeechRequest(BaseModel):
    input: Optional[str] = None
    text: Optional[str] = None
    voice: Optional[str] = None
    speaker: Optional[str] = None
    speaker_wav: Optional[str] = None
    language: str = "en"
    response_format: str = "wav"


def find_xtts_model(script_dir: Path) -> Optional[Path]:
    candidates = [
        script_dir / "multi-dataset--xtts_v2",
        script_dir.parent / "multi-dataset--xtts_v2",
        script_dir.parent / "TTS" / "multi-dataset--xtts_v2",
    ]
    for candidate in candidates:
        if (candidate / "config.json").exists() and ((candidate / "model.pth").exists() or (candidate / "model.safetensors").exists()):
            return candidate
    return None


def load_model():
    global tts_engine
    if tts_engine is not None:
        return tts_engine
    try:
        from TTS.api import TTS
    except Exception as exc:
        raise RuntimeError(f"Python package TTS is not installed in this environment: {exc}") from exc

    model_dir = Path(settings.model_dir) if settings.model_dir else find_xtts_model(Path(__file__).resolve().parent)
    if model_dir is None:
        model_name = f"tts_models/multilingual/multi-dataset/xtts_v{settings.model_version}"
        tts_engine = TTS(model_name=model_name)
    else:
        config_path = model_dir / "config.json"
        tts_engine = TTS(model_path=str(model_dir), config_path=str(config_path))
    if hasattr(tts_engine, "to"):
        tts_engine = tts_engine.to(settings.device)
    return tts_engine


def voice_candidates() -> list[str]:
    voice_dir = Path(settings.voice_dir) if settings.voice_dir else Path(settings.output_dir).parent / "voices"
    if not voice_dir.exists():
        return []
    return [str(path) for path in voice_dir.glob("*") if path.suffix.lower() in {".wav", ".mp3", ".flac"}]


@app.get("/health")
def health():
    return {"ok": True, "model_loaded": tts_engine is not None}


@app.get("/voices")
def voices():
    return {"voices": voice_candidates()}


@app.get("/v1/audio/voices")
def openai_voices():
    return {"data": [{"id": path, "name": Path(path).stem} for path in voice_candidates()]}


@app.post("/v1/audio/speech")
def speech(request: SpeechRequest):
    text = request.input or request.text
    if not text:
        raise HTTPException(status_code=400, detail="input or text is required")
    speaker_wav = request.speaker_wav or request.voice or request.speaker
    if not speaker_wav:
        voices = voice_candidates()
        speaker_wav = voices[0] if voices else None
    if not speaker_wav:
        raise HTTPException(status_code=400, detail="speaker_wav or a voice sample is required")

    engine = load_model()
    output_dir = Path(settings.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    output_path = output_dir / f"aether-{int(time.time())}-{uuid.uuid4().hex}.wav"
    engine.tts_to_file(text=text, speaker_wav=speaker_wav, language=request.language, file_path=str(output_path))
    return FileResponse(str(output_path), media_type="audio/wav", filename=output_path.name)


def parse_args():
    parser = argparse.ArgumentParser(description="Aether XTTS v2 API server")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8020)
    parser.add_argument("--model-dir", default=DEFAULT_MODEL_DIR)
    parser.add_argument("--output-dir", default=DEFAULT_OUTPUT_DIR or str(Path.cwd() / "output"))
    parser.add_argument("--voice-dir", default=None)
    parser.add_argument("--model-version", default="2.0.3")
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--preload", action="store_true")
    return parser.parse_args()


if __name__ == "__main__":
    settings = parse_args()
    if settings.preload:
        load_model()
    uvicorn.run(app, host=settings.host, port=settings.port)
""";
    }

    private static void AddItem(
        List<LocalAiReadinessItem> items,
        string key,
        string label,
        string path,
        string ready,
        string missing,
        bool required)
    {
        items.Add(new LocalAiReadinessItem(
            key,
            label,
            string.IsNullOrWhiteSpace(path) ? (required ? LocalAiReadinessStatus.Missing : LocalAiReadinessStatus.Optional) : LocalAiReadinessStatus.Found,
            string.IsNullOrWhiteSpace(path) ? missing : path,
            ready,
            required));
    }

    private static LocalAiSetupAction CreateVenvAction(string target) =>
        new("create-venv", LocalAiSetupActionKind.CreateVenv, "Create Python venv", target,
            [DefaultPythonCommand(), "-m", "venv", target], LocalAiSetupRiskLevel.Medium,
            "Creates an isolated Python environment under the selected AI folder.", false, true, true);

    private static LocalAiSetupAction InstallXttsAction(string pythonPath, bool canRun) =>
        new("install-xtts", LocalAiSetupActionKind.InstallXttsDependencies, "Install XTTS packages", pythonPath,
            [pythonPath, "-m", "pip", "install", ..XttsPackages], LocalAiSetupRiskLevel.High,
            canRun
                ? "Installs Python packages into the selected venv. This may use the network."
                : "Create or choose a venv before installing XTTS packages.",
            true, true, canRun);

    private static LocalAiSetupAction CreateScriptAction(string target) =>
        new("create-xtts-script", LocalAiSetupActionKind.CreateXttsApiScript, "Create XTTS API script", target,
            ["write-file", target], LocalAiSetupRiskLevel.Medium,
            "Creates a local FastAPI script for XTTS v2 without starting it.", false, true, true);

    private static LocalAiSetupAction CreateDirectoryAction(string id, string title, string target) =>
        new($"create-{id}", LocalAiSetupActionKind.CreateDirectory, title, target,
            ["mkdir", target], LocalAiSetupRiskLevel.Low,
            "Creates the folder if it does not already exist.", false, true, true);

    private static async Task<LocalAiSetupResult> CreateVenvAsync(string target, IProgress<string>? progress, CancellationToken ct)
    {
        if (Directory.Exists(target) && File.Exists(PythonPathForVenv(target)))
            return new LocalAiSetupResult(true, $"Venv already exists at {target}", PythonPathForVenv(target));

        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? target);
        var pythonCommand = await ResolveCompatiblePythonCommandAsync(progress, ct);
        if (pythonCommand is null)
            return new LocalAiSetupResult(false,
                "No compatible Python interpreter was found for XTTS. Install Python 3.9, 3.10, or 3.11 and retry.");

        var result = await RunProcessAsync(
            pythonCommand.FileName,
            [.. pythonCommand.PrefixArgs, "-m", "venv", target],
            Path.GetDirectoryName(target) ?? Environment.CurrentDirectory,
            progress,
            ct);
        return result with { UpdatedPath = PythonPathForVenv(target) };
    }

    private static Task<LocalAiSetupResult> InstallXttsAsync(string pythonPath, IProgress<string>? progress, CancellationToken ct)
    {
        if (!File.Exists(pythonPath))
            return Task.FromResult(new LocalAiSetupResult(false, $"Python was not found at {pythonPath}. Create or choose a venv first."));

        return InstallXttsWithRepairAsync(pythonPath, progress, ct);
    }

    private static async Task<LocalAiSetupResult> InstallXttsWithRepairAsync(string pythonPath, IProgress<string>? progress, CancellationToken ct)
    {
        var workingDirectory = Path.GetDirectoryName(pythonPath) ?? Environment.CurrentDirectory;
        var preflight = await RunProcessAsync(
            pythonPath,
            ["-c", "import encodings, sys; print(sys.executable)"],
            workingDirectory,
            progress,
            ct);

        if (!preflight.Success)
        {
            progress?.Report("Detected a broken Python runtime. Aether will rebuild this venv and retry installation.");
            var repair = await RebuildVenvAsync(pythonPath, progress, ct);
            if (!repair.Success)
            {
                var combinedLog = $"{preflight.Log.TrimEnd()}{Environment.NewLine}{repair.Log}";
                return new LocalAiSetupResult(false, combinedLog, repair.UpdatedPath);
            }

            pythonPath = repair.UpdatedPath ?? pythonPath;
            workingDirectory = Path.GetDirectoryName(pythonPath) ?? Environment.CurrentDirectory;
        }

        var isValid = await ValidatePythonForXttsAsync(pythonPath, workingDirectory, progress, ct);
        if (!isValid)
        {
            progress?.Report("Python validation failed. Attempting venv rebuild...");
            var repair = await RebuildVenvAsync(pythonPath, progress, ct);
            if (!repair.Success)
                return repair;

            pythonPath = repair.UpdatedPath ?? pythonPath;
            workingDirectory = Path.GetDirectoryName(pythonPath) ?? Environment.CurrentDirectory;

            isValid = await ValidatePythonForXttsAsync(pythonPath, workingDirectory, progress, ct);
            if (!isValid)
            {
                return new LocalAiSetupResult(false,
                    "XTTS requires a valid Python 3.9-3.11 installation with encodings, venv module, and proper sys.prefix. Please install Python 3.9-3.11 on your system.");
            }
        }

        return await RunProcessAsync(
            pythonPath,
            ["-m", "pip", "install", ..XttsPackages],
            workingDirectory,
            progress,
            ct);
    }

    private static async Task<LocalAiSetupResult> RebuildVenvAsync(string pythonPath, IProgress<string>? progress, CancellationToken ct)
    {
        var venvRoot = GetVenvRootFromPythonPath(pythonPath);
        if (string.IsNullOrWhiteSpace(venvRoot) || !Directory.Exists(venvRoot))
            return new LocalAiSetupResult(false, $"Cannot repair Python runtime at {pythonPath}. Select a venv Python path and retry.");

        var parent = Path.GetDirectoryName(venvRoot);
        if (string.IsNullOrWhiteSpace(parent))
            return new LocalAiSetupResult(false, $"Cannot determine parent folder for venv at {venvRoot}.");

        var backup = venvRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + $".broken-{DateTime.UtcNow:yyyyMMddHHmmss}";

        Directory.Move(venvRoot, backup);
        progress?.Report($"Backed up broken venv: {backup}");

        var pythonCommand = await ResolveCompatiblePythonCommandAsync(progress, ct);
        if (pythonCommand is null)
            return new LocalAiSetupResult(false,
                "No compatible Python interpreter was found for XTTS rebuild. Install Python 3.9, 3.10, or 3.11 and retry.");

        var recreate = await RunProcessAsync(
            pythonCommand.FileName,
            [.. pythonCommand.PrefixArgs, "-m", "venv", venvRoot],
            parent,
            progress,
            ct);

        if (!recreate.Success)
            return recreate;

        var repairedPython = PythonPathForVenv(venvRoot);
        if (!File.Exists(repairedPython))
            return new LocalAiSetupResult(false, $"Venv rebuild finished but Python was not found at {repairedPython}.");

        return new LocalAiSetupResult(true, $"Rebuilt Python venv at {venvRoot}", repairedPython);
    }

    private static string GetVenvRootFromPythonPath(string pythonPath)
    {
        if (string.IsNullOrWhiteSpace(pythonPath))
            return string.Empty;

        var binDir = Path.GetDirectoryName(pythonPath);
        if (string.IsNullOrWhiteSpace(binDir))
            return string.Empty;

        return Path.GetDirectoryName(binDir) ?? string.Empty;
    }

    private static async Task<Version?> ReadPythonVersionAsync(string pythonPath, string workingDirectory, CancellationToken ct)
    {
        var result = await RunProcessAsync(
            pythonPath,
            ["-c", "import sys; print(f'AETHER_PYVER={sys.version_info[0]}.{sys.version_info[1]}')"],
            workingDirectory,
            progress: null,
            ct);

        if (!result.Success)
            return null;

        var match = Regex.Match(result.Log, @"AETHER_PYVER=(\d+)\.(\d+)", RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups[1].Value, out var major) || !int.TryParse(match.Groups[2].Value, out var minor))
            return null;

        return new Version(major, minor);
    }

    private static bool IsXttsCompatibleVersion(Version version) =>
        version >= MinSupportedXttsPython && version < MaxSupportedXttsPythonExclusive;

    private static async Task<bool> ValidatePythonForXttsAsync(string pythonPath, string workingDirectory, IProgress<string>? progress, CancellationToken ct)
    {
        var validationScript = """
import sys
import tempfile
import os

checks = []

# 1. Execute successfully (implicit - we're running)
checks.append(("Execute", True))

# 2. Report version 3.11.x (or compatible range)
version = f"{sys.version_info[0]}.{sys.version_info[1]}"
checks.append(("Version detection", True))
print(f"AETHER_VERSION={version}")

# 3. Import encodings
try:
    import encodings
    checks.append(("Import encodings", True))
except Exception as e:
    checks.append(("Import encodings", False))
    print(f"AETHER_ERROR=Failed to import encodings: {e}")

# 4. Import venv
try:
    import venv
    checks.append(("Import venv", True))
except Exception as e:
    checks.append(("Import venv", False))
    print(f"AETHER_ERROR=Failed to import venv: {e}")

# 5. Check sys.prefix/sys.base_prefix are valid
bad_prefixes = ["/install", ""]
base_prefix = getattr(sys, "base_prefix", sys.prefix)
if sys.prefix in bad_prefixes or base_prefix in bad_prefixes:
    checks.append(("Valid sys.prefix", False))
    print(f"AETHER_ERROR=Invalid sys.prefix ({sys.prefix}) or base_prefix ({base_prefix})")
else:
    checks.append(("Valid sys.prefix", True))

# 6. Create test venv
try:
    with tempfile.TemporaryDirectory() as tmpdir:
        test_venv = os.path.join(tmpdir, "test_venv")
        builder = venv.EnvBuilder()
        builder.create(test_venv)
        test_python = os.path.join(test_venv, "bin" if not sys.platform.startswith("win") else "Scripts", "python.exe" if sys.platform.startswith("win") else "python")
        if os.path.exists(test_python):
            checks.append(("Create test venv", True))
        else:
            checks.append(("Create test venv", False))
            print(f"AETHER_ERROR=Test venv created but python not found at {test_python}")
except Exception as e:
    checks.append(("Create test venv", False))
    print(f"AETHER_ERROR=Failed to create test venv: {e}")

# Report all checks
for check_name, passed in checks:
    status = "PASS" if passed else "FAIL"
    print(f"AETHER_CHECK={check_name}={status}")
""";

        var result = await RunProcessAsync(
            pythonPath,
            ["-c", validationScript],
            workingDirectory,
            progress: null,
            ct);

        if (!result.Success)
        {
            progress?.Report($"Python validation failed to execute: {result.Log}");
            return false;
        }

        var checks = new Dictionary<string, bool>();
        var errors = new List<string>();

        foreach (var line in result.Log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("AETHER_CHECK="))
            {
                var parts = line.Substring("AETHER_CHECK=".Length).Split('=');
                if (parts.Length == 2)
                {
                    checks[parts[0]] = parts[1] == "PASS";
                }
            }
            else if (line.StartsWith("AETHER_ERROR="))
            {
                errors.Add(line.Substring("AETHER_ERROR=".Length));
            }
            else if (line.StartsWith("AETHER_VERSION="))
            {
                var version = line.Substring("AETHER_VERSION=".Length);
                progress?.Report($"Validating Python {version}...");
            }
        }

        var allPassed = checks.Values.All(v => v);

        if (!allPassed)
        {
            progress?.Report("Python validation failed:");
            foreach (var (check, passed) in checks)
            {
                if (!passed)
                    progress?.Report($"  - {check}: FAILED");
            }
            foreach (var error in errors)
                progress?.Report($"  - {error}");
        }
        else
        {
            progress?.Report("Python validation passed all checks.");
        }

        return allPassed;
    }

    private static async Task<PythonCommand?> ResolveCompatiblePythonCommandAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var candidates = GetPythonCommandCandidates();
        var preferredCandidates = candidates.Take(candidates.Count - 1).ToList();
        var fallbackCandidate = candidates.Last();

        foreach (var candidate in preferredCandidates)
        {
            var testPython = candidate.FileName;
            if (candidate.PrefixArgs.Count > 0)
            {
                testPython = $"{candidate.FileName} {string.Join(" ", candidate.PrefixArgs)}".Trim();
            }

            progress?.Report($"Testing {candidate.DisplayName}...");
            if (!await ValidatePythonForXttsAsync(candidate.FileName, Environment.CurrentDirectory, progress, ct))
                continue;

            progress?.Report($"Using Python for XTTS setup: {candidate.DisplayName}");
            return candidate;
        }

        progress?.Report($"Testing {fallbackCandidate.DisplayName}...");
        if (await ValidatePythonForXttsAsync(fallbackCandidate.FileName, Environment.CurrentDirectory, progress, ct))
        {
            progress?.Report($"Preferred Python 3.9-3.11 not found. Using {fallbackCandidate.DisplayName}. This may have compatibility issues.");
            return fallbackCandidate;
        }

        progress?.Report("No compatible Python interpreter found. XTTS requires Python 3.9-3.11. Please install one of these versions.");
        return null;
    }

    private static IReadOnlyList<PythonCommand> GetPythonCommandCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            return
            [
                new PythonCommand("py", ["-3.11"], "py -3.11"),
                new PythonCommand("py", ["-3.10"], "py -3.10"),
                new PythonCommand("py", ["-3.9"], "py -3.9"),
                new PythonCommand("python", [], "python")
            ];
        }

        return
        [
            new PythonCommand("python3.11", [], "python3.11"),
            new PythonCommand("python3.10", [], "python3.10"),
            new PythonCommand("python3.9", [], "python3.9"),
            new PythonCommand("python3", [], "python3")
        ];
    }

    private LocalAiSetupResult CreateXttsApiScript(string target, AppSettings settings, bool allowOverwrite, IProgress<string>? progress)
    {
        if (File.Exists(target) && !allowOverwrite)
            return new LocalAiSetupResult(false, $"Refused to overwrite existing script at {target}.");

        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? Environment.CurrentDirectory);
        File.WriteAllText(target, BuildXttsApiScript(settings.TtsModelDirectory, settings.TtsOutputDirectory));
        progress?.Report($"Wrote {target}");
        return new LocalAiSetupResult(true, $"Created XTTS API script at {target}", target);
    }

    private static LocalAiSetupResult CreateSupportDirectory(string target, IProgress<string>? progress)
    {
        Directory.CreateDirectory(target);
        progress?.Report($"Ready: {target}");
        return new LocalAiSetupResult(true, $"Folder ready at {target}", target);
    }

    private static async Task<LocalAiSetupResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var log = new StringBuilder();
        log.AppendLine($"Command: {fileName} {string.Join(" ", args.Select(QuoteIfNeeded))}");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => AppendLine(e.Data, log, progress);
        process.ErrorDataReceived += (_, e) => AppendLine(e.Data, log, progress);

        if (!process.Start())
            return new LocalAiSetupResult(false, $"Failed to start {fileName}.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);
        return new LocalAiSetupResult(process.ExitCode == 0, log.ToString());
    }

    private static void AppendLine(string? line, StringBuilder log, IProgress<string>? progress)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (log)
            log.AppendLine(line);
        progress?.Report(line);
    }

    private static string PythonPathForVenv(string venv) =>
        Path.Combine(venv, OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python");

    private static string DefaultPythonCommand() => OperatingSystem.IsWindows() ? "python" : "python3";

    private sealed record PythonCommand(string FileName, IReadOnlyList<string> PrefixArgs, string DisplayName);

    private static string QuoteIfNeeded(string value) =>
        value.Contains(" ", StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
