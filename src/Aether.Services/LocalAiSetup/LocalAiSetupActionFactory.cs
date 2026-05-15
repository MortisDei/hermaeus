using Aether.Core.Models;

namespace Aether.Services;

internal static class LocalAiSetupActionFactory
{
    internal static LocalAiSetupAction CreateVenvAction(string target) =>
        new("create-venv", LocalAiSetupActionKind.CreateVenv, "Create Python venv", target,
            [DefaultPythonCommand(), "-m", "venv", target], LocalAiSetupRiskLevel.Medium,
            "Creates an isolated Python environment under the selected AI folder.", false, true, true);

    internal static LocalAiSetupAction InstallXttsAction(string pythonPath, bool canRun) =>
        new("install-voice-backend", LocalAiSetupActionKind.InstallXttsDependencies, "Install Voice Backend Packages", pythonPath,
            [pythonPath, "-m", "pip", "install", ..LocalAiSetupConstants.XttsPackages], LocalAiSetupRiskLevel.High,
            canRun
                ? "Installs voice backend packages into the selected venv. This may use the network."
                : "Create or choose a venv before installing voice backend packages.",
            true, true, canRun);

    internal static LocalAiSetupAction CreateScriptAction(string target) =>
        new("create-xtts-script", LocalAiSetupActionKind.CreateXttsApiScript, "Create XTTS API script", target,
            ["write-file", target], LocalAiSetupRiskLevel.Medium,
            "Creates a local FastAPI script for XTTS v2 without starting it.", false, true, true);

    internal static LocalAiSetupAction CreateDirectoryAction(string id, string title, string target) =>
        new($"create-{id}", LocalAiSetupActionKind.CreateDirectory, title, target,
            ["mkdir", target], LocalAiSetupRiskLevel.Low,
            "Creates the folder if it does not already exist.", false, true, true);

    internal static LocalAiSetupAction DownloadGgufModelAction(string modelPath, string url) =>
        new("download-phi4-model", LocalAiSetupActionKind.DownloadGgufModel,
            "Download Phi-4 Mini Reasoning Model",
            modelPath,
            [url],
            LocalAiSetupRiskLevel.Medium,
            "Downloads the Phi-4 mini reasoning GGUF model (Q5_K_M, ~9GB) for local reasoning.",
            true, true, true);

    internal static LocalAiSetupAction DownloadTtsModelAction(string modelPath, string url) =>
        new("download-kokoro-model", LocalAiSetupActionKind.DownloadTtsModel,
            "Download Kokoro TTS Model",
            modelPath,
            [url],
            LocalAiSetupRiskLevel.Medium,
            "Downloads the Kokoro-82M TTS model for fast local speech synthesis.",
            true, true, true);

    internal static LocalAiSetupAction DownloadLlamaServerAction(string installPath) =>
        new("download-llama-server", LocalAiSetupActionKind.DownloadLlamaServer,
            "Download llama-server Binary",
            Path.Combine(installPath, OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server"),
            [LocalAiSetupConstants.LlamaCppReleasesUrl],
            LocalAiSetupRiskLevel.Medium,
            "Downloads the llama-server binary for running local LLMs.",
            true, true, true);

    private static string DefaultPythonCommand() =>
        OperatingSystem.IsWindows() ? "python" : "python3";
}
