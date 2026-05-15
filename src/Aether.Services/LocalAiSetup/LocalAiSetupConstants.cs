namespace Aether.Services;

internal static class LocalAiSetupConstants
{
    internal const string Phi4MiniReasoningQ5KmUrl = "https://huggingface.co/bartowski/microsoft_Phi-4-mini-reasoning-GGUF/resolve/main/microsoft_Phi-4-mini-reasoning-Q5_K_M.gguf?download=true";
    internal const string LlamaCppReleasesUrl = "https://github.com/ggerganov/llama.cpp/releases";
    internal static readonly string[] XttsPackages = ["TTS", "fastapi", "uvicorn", "soundfile"];
}
