using Aether.Core.Models;

namespace Aether.Services;

public sealed record LocalAiAssetLayout(
    string Root,
    string ModelsDirectory,
    string TtsScriptPath,
    string TtsPythonPath,
    string TtsModelDirectory,
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
    public static IReadOnlyList<string> FindGgufModels(string root)
    {
        var layout = Detect(root);
        if (string.IsNullOrWhiteSpace(layout.ModelsDirectory) || !Directory.Exists(layout.ModelsDirectory))
            return [];

        try
        {
            return Directory.EnumerateFiles(layout.ModelsDirectory, "*.gguf", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static LocalAiAssetLayout Detect(string root)
    {
        root = root.Trim();
        if (string.IsNullOrWhiteSpace(root))
            return new LocalAiAssetLayout(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0);

        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            return new LocalAiAssetLayout(root, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0);

        var models = FindModelsDirectory(root);
        var script = FindFirstFile(root, "xtts_api_server.py");
        var python = FindPython(root);
        var ttsModel = FindXttsModelDirectory(root);
        var voices = FirstExistingDirectory(
            Path.Combine(root, "TTS", "voices"),
            Path.Combine(root, "tts", "voices"),
            Path.Combine(root, "tts", "speakers"),
            Path.Combine(root, "voices"),
            Path.Combine(root, "speakers"));
        var output = FirstExistingDirectory(
            Path.Combine(root, "TTS", "output"),
            Path.Combine(root, "tts", "output"),
            Path.Combine(root, "tts", "outputs"),
            Path.Combine(root, "output"));
        var reranker = FindRerankerDirectory(root);

        var count = new[] { models, script, python, ttsModel, voices, output, reranker }.Count(p => !string.IsNullOrWhiteSpace(p));
        return new LocalAiAssetLayout(root, models, script, python, ttsModel, voices, output, reranker, count);
    }

    public static void ApplyDetected(AppSettings settings, bool overwrite = false)
    {
        var layout = Detect(settings.DataManagement.LocalAiAssetsRoot);
        if (string.IsNullOrWhiteSpace(layout.Root))
            return;

        if (overwrite || string.IsNullOrWhiteSpace(settings.Tts.ScriptPath))
            settings.Tts.ScriptPath = layout.TtsScriptPath;
        if (overwrite || string.IsNullOrWhiteSpace(settings.Tts.PythonPath))
            settings.Tts.PythonPath = layout.TtsPythonPath;
        if (overwrite || string.IsNullOrWhiteSpace(settings.Tts.ModelDirectory))
            settings.Tts.ModelDirectory = layout.TtsModelDirectory;
        if (overwrite || string.IsNullOrWhiteSpace(settings.Tts.VoiceDirectory))
            settings.Tts.VoiceDirectory = layout.TtsVoiceDirectory;
        if (overwrite || string.IsNullOrWhiteSpace(settings.Tts.OutputDirectory))
            settings.Tts.OutputDirectory = layout.TtsOutputDirectory;
        if (overwrite || string.IsNullOrWhiteSpace(settings.Rag.RerankerModelPath))
            settings.Rag.RerankerModelPath = layout.RerankerDirectory;
    }

    private static string FirstExistingDirectory(params string[] candidates) =>
        candidates.FirstOrDefault(Directory.Exists) ?? string.Empty;

    private static string FindModelsDirectory(string root)
    {
        var candidates = new List<string>
        {
            Path.Combine(root, "Models"),
            Path.Combine(root, "models"),
            Path.Combine(root, "gguf")
        };

        try
        {
            candidates.AddRange(Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(Path.GetFileName(path), "models", StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            // Fall back to the standard candidate list.
        }

        candidates = candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var withGguf = candidates
            .Where(Directory.Exists)
            .Select(path => new { Path = path, Count = CountGgufFiles(path) })
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => string.Equals(Path.GetFileName(x.Path), "Models", StringComparison.Ordinal) ? 0 : 1)
            .FirstOrDefault();

        if (withGguf is not null)
            return withGguf.Path;

        return candidates.FirstOrDefault(Directory.Exists) ?? string.Empty;
    }

    private static int CountGgufFiles(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*.gguf", SearchOption.AllDirectories).Count();
        }
        catch
        {
            return 0;
        }
    }

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
        var direct = Path.Combine(root, "venv", OperatingSystem.IsWindows() ? "Scripts" : "bin", fileName);
        if (File.Exists(direct))
            return direct;

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

    private static string FindXttsModelDirectory(string root)
    {
        var direct = Path.Combine(root, "TTS", "multi-dataset--xtts_v2");
        if (LooksLikeXttsModel(direct))
            return direct;

        try
        {
            return Directory.EnumerateDirectories(root, "*xtts*", SearchOption.AllDirectories)
                .FirstOrDefault(LooksLikeXttsModel) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool LooksLikeXttsModel(string path) =>
        Directory.Exists(path)
        && File.Exists(Path.Combine(path, "config.json"))
        && (File.Exists(Path.Combine(path, "model.pth")) || File.Exists(Path.Combine(path, "model.safetensors")));

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
