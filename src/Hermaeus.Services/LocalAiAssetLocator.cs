using Hermaeus.Core.Models;

namespace Hermaeus.Services;

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
                .Where(path => !IsUnderSpecialModelDirectory(path, layout.ModelsDirectory))
                .Where(path => !IsCompanionGguf(path))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// r18 03-model-catalog-and-memory-ui.md 3.2: the reported "small model files &lt; ~500 MB
    /// cluttering the list" turned out, verified against a real HF hub cache, not to be sharded
    /// GGUF fragments (no <c>-00001-of-000NN.gguf</c> files were present anywhere) but companion
    /// files that HF repos ship alongside the real chat model: <c>mmproj*.gguf</c> vision
    /// projectors (llama.cpp/clip.cpp's own naming convention, consumed via <c>--mmproj</c>, an
    /// ExtraArgs-only flag with no first-class UI in Hermaeus) and <c>mtp-*.gguf</c>
    /// multi-token-prediction draft weights (Unsloth's naming convention for a companion
    /// speculative-decoding file, also unused by Hermaeus - see doc 04 4.4's rejection of
    /// draft-model speculative decoding this round). Neither is loadable as a standalone chat
    /// model, so they are excluded here rather than merely labeled.
    /// </summary>
    private static bool IsCompanionGguf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("mtp-", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> FindEmbeddingModels(string root)
    {
        root = root.Trim();
        if (string.IsNullOrWhiteSpace(root))
            return [];

        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            return [];

        var models = FindModelsDirectory(root);
        if (string.IsNullOrWhiteSpace(models) || !Directory.Exists(models))
            return [];

        try
        {
            var dedicated = GetEmbeddingDirectories(models)
                .Where(Directory.Exists)
                .SelectMany(path => Directory.EnumerateFiles(path, "*.gguf", SearchOption.AllDirectories))
                .Where(LooksLikeEmbeddingGguf)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (dedicated.Count > 0)
                return dedicated;

            return Directory.EnumerateFiles(models, "*.gguf", SearchOption.TopDirectoryOnly)
                .Where(LooksLikeEmbeddingGguf)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Best-guess folder to open a file picker in for an embedding model:
    /// the first existing embed/embedding/embeddings subdirectory under the
    /// detected models folder, falling back to the models folder itself (or
    /// root) so browsing never lands the user in an unrelated location like
    /// the chat models folder.
    /// </summary>
    public static string GetPreferredEmbeddingsDirectory(string root)
    {
        root = root.Trim();
        if (string.IsNullOrWhiteSpace(root))
            return string.Empty;

        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            return string.Empty;

        var models = FindModelsDirectory(root);
        if (string.IsNullOrWhiteSpace(models))
            return root;

        return GetEmbeddingDirectories(models).FirstOrDefault(Directory.Exists) ?? models;
    }

    public static IReadOnlyList<string> FindRerankerDirectories(string root)
    {
        root = root.Trim();
        if (string.IsNullOrWhiteSpace(root))
            return [];

        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            return [];

        var models = FindModelsDirectory(root);
        if (string.IsNullOrWhiteSpace(models) || !Directory.Exists(models))
            return [];

        var searchRoots = new[]
        {
            Path.Combine(models, "rerank"),
            Path.Combine(models, "reranker")
        };

        try
        {
            var modelRerankers = searchRoots
                .Where(Directory.Exists)
                .SelectMany(path => Directory.EnumerateFiles(path, "*.onnx", SearchOption.AllDirectories))
                .Select(Path.GetDirectoryName)
                .Where(LooksLikeRerankerDirectory)
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (modelRerankers.Count > 0)
                return modelRerankers;

            return Directory.EnumerateFiles(root, "*.onnx", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Where(LooksLikeRerankerDirectory)
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
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
        // Actual on-disk directories go first so case-insensitive dedup keeps
        // the real casing instead of a guessed "Models"/"models" variant.
        var candidates = new List<string>();
        try
        {
            candidates.AddRange(Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(Path.GetFileName(path), "models", StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            // Fall back to the standard candidate list.
        }

        candidates.Add(Path.Combine(root, "Models"));
        candidates.Add(Path.Combine(root, "models"));
        candidates.Add(Path.Combine(root, "gguf"));

        // Case-insensitive dedup on Windows collapses guessed variants onto the
        // real directory; on case-sensitive filesystems "Models" and "models"
        // are genuinely different directories and must both stay candidates.
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        candidates = candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(pathComparer)
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
        var discovered = FindRerankerDirectories(root).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(discovered))
            return discovered;

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

    private static bool LooksLikeRerankerDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        try
        {
            return File.Exists(Path.Combine(path, "vocab.txt"))
                && Directory.EnumerateFiles(path, "*.onnx", SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> GetEmbeddingDirectories(string modelsDirectory)
    {
        yield return Path.Combine(modelsDirectory, "embed");
        yield return Path.Combine(modelsDirectory, "embedding");
        yield return Path.Combine(modelsDirectory, "embeddings");
    }

    private static bool LooksLikeEmbeddingGguf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Contains("embed", StringComparison.OrdinalIgnoreCase)
            || name.Contains("embedding", StringComparison.OrdinalIgnoreCase)
            || name.Contains("nomic", StringComparison.OrdinalIgnoreCase)
            || name.Contains("bge", StringComparison.OrdinalIgnoreCase)
            || name.Contains("gte", StringComparison.OrdinalIgnoreCase)
            || name.Equals("e5", StringComparison.OrdinalIgnoreCase)
            || name.Contains("e5-", StringComparison.OrdinalIgnoreCase)
            || name.Contains("e5_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderSpecialModelDirectory(string path, string modelsDirectory)
    {
        var relative = Path.GetRelativePath(modelsDirectory, path);
        var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault();
        return firstSegment is not null
            && (firstSegment.Equals("embed", StringComparison.OrdinalIgnoreCase)
                || firstSegment.Equals("embedding", StringComparison.OrdinalIgnoreCase)
                || firstSegment.Equals("embeddings", StringComparison.OrdinalIgnoreCase)
                || firstSegment.Equals("rerank", StringComparison.OrdinalIgnoreCase)
                || firstSegment.Equals("reranker", StringComparison.OrdinalIgnoreCase));
    }
}
