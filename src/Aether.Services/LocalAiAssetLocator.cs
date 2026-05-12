using Aether.Core.Models;

namespace Aether.Services;

public sealed record LocalAiAssetLayout(
    string Root,
    string ModelsDirectory,
    string TtsScriptPath,
    string TtsPythonPath,
    string TtsVoiceDirectory,
    string TtsOutputDirectory,
    string RerankerDirectory,
    int FoundCount)
{
    public string Summary => string.IsNullOrWhiteSpace(Root)
        ? "Choose a local AI assets folder first."
        : FoundCount == 0
            ? $"No known assets found under {Root}."
            : $"Found {FoundCount} asset location(s) under {Root}.";
}

public static class LocalAiAssetLocator
{
    public static LocalAiAssetLayout Detect(string root)
    {
        root = root.Trim();
        if (string.IsNullOrWhiteSpace(root))
            return new LocalAiAssetLayout(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0);

        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            return new LocalAiAssetLayout(root, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0);

        var models = FirstExistingDirectory(
            Path.Combine(root, "models"),
            Path.Combine(root, "Models"),
            Path.Combine(root, "gguf"));
        var script = FindFirstFile(root, "xtts_api_server.py");
        var python = FindPython(root);
        var voices = FirstExistingDirectory(
            Path.Combine(root, "tts", "voices"),
            Path.Combine(root, "tts", "speakers"),
            Path.Combine(root, "voices"),
            Path.Combine(root, "speakers"));
        var output = FirstExistingDirectory(
            Path.Combine(root, "tts", "output"),
            Path.Combine(root, "tts", "outputs"),
            Path.Combine(root, "output"));
        var reranker = FindRerankerDirectory(root);

        var count = new[] { models, script, python, voices, output, reranker }.Count(p => !string.IsNullOrWhiteSpace(p));
        return new LocalAiAssetLayout(root, models, script, python, voices, output, reranker, count);
    }

    public static void ApplyDetected(AppSettings settings, bool overwrite = false)
    {
        var layout = Detect(settings.LocalAiAssetsRoot);
        if (string.IsNullOrWhiteSpace(layout.Root))
            return;

        if (overwrite || string.IsNullOrWhiteSpace(settings.TtsScriptPath))
            settings.TtsScriptPath = layout.TtsScriptPath;
        if (overwrite || string.IsNullOrWhiteSpace(settings.TtsPythonPath))
            settings.TtsPythonPath = layout.TtsPythonPath;
        if (overwrite || string.IsNullOrWhiteSpace(settings.TtsVoiceDirectory))
            settings.TtsVoiceDirectory = layout.TtsVoiceDirectory;
        if (overwrite || string.IsNullOrWhiteSpace(settings.TtsOutputDirectory))
            settings.TtsOutputDirectory = layout.TtsOutputDirectory;
        if (overwrite || string.IsNullOrWhiteSpace(settings.RagRerankerModelPath))
            settings.RagRerankerModelPath = layout.RerankerDirectory;
    }

    private static string FirstExistingDirectory(params string[] candidates) =>
        candidates.FirstOrDefault(Directory.Exists) ?? string.Empty;

    private static string FindFirstFile(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FindPython(string root)
    {
        var fileName = OperatingSystem.IsWindows() ? "python.exe" : "python";
        try
        {
            return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}venv{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FindRerankerDirectory(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "model_O4.onnx", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path!, "vocab.txt")))
                .FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
