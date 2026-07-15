using System.Collections.Concurrent;
using System.Text;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Voice;

/// <summary>
/// User-editable pronunciation overrides at <c>{DataRoot}/voice/lexicon.txt</c>,
/// one <c>word = ipa</c> entry per line. Consulted before CMUdict so a user
/// can always correct a mispronunciation. Reloaded whenever the file's
/// mtime changes; a missing file is seeded with defaults for words this app
/// introduces that no general-purpose dictionary would know (its own name
/// among them).
/// </summary>
internal static class KokoroUserLexicon
{
    private sealed record CacheEntry(DateTime Mtime, IReadOnlyDictionary<string, string> Words);

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    public static readonly IReadOnlyList<(string Word, string Ipa)> Defaults =
    [
        ("aether", "ˈiθɚ"),
        ("ollama", "oʊˈlɑmə"),
        ("kokoro", "koʊˈkoʊɹoʊ"),
        ("llama", "ˈlɑmə"),
        ("qwen", "kwɛn")
    ];

    public static bool TryGetIpa(string? lexiconPath, string word, out string ipa, IRuntimeLogService? logs = null)
    {
        ipa = string.Empty;
        if (string.IsNullOrWhiteSpace(lexiconPath))
            return false;

        return GetOrLoad(lexiconPath, logs).TryGetValue(word, out ipa!);
    }

    public static bool Contains(string? lexiconPath, string word, IRuntimeLogService? logs = null) =>
        TryGetIpa(lexiconPath, word, out _, logs);

    private static IReadOnlyDictionary<string, string> GetOrLoad(string path, IRuntimeLogService? logs)
    {
        DateTime mtime;
        try
        {
            if (!File.Exists(path))
                WriteDefaults(path);

            mtime = File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return Empty;
        }

        if (Cache.TryGetValue(path, out var cached) && cached.Mtime == mtime)
            return cached.Words;

        var (parsed, invalidLines) = ParseFile(path);
        Cache[path] = new CacheEntry(mtime, parsed);

        if (logs is not null)
            foreach (var lineNumber in invalidLines)
                logs.Add(new RuntimeLogEntry(
                    DateTime.UtcNow,
                    RuntimeLogLevel.Warning,
                    RuntimeLogCategory.Voice,
                    $"Pronunciation lexicon line {lineNumber} is invalid and was skipped: {path}"));

        return parsed;
    }

    private static void WriteDefaults(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("# Aether pronunciation lexicon.");
            sb.AppendLine("# One entry per line: word = ipa");
            sb.AppendLine("# IPA must use symbols from Kokoro's vocabulary; invalid lines are skipped and logged.");
            foreach (var (word, ipa) in Defaults)
                sb.AppendLine($"{word} = {ipa}");

            File.WriteAllText(path, sb.ToString());
        }
        catch
        {
            // Best-effort seed; a failure here just means no user lexicon exists yet.
        }
    }

    private static (Dictionary<string, string> Words, List<int> InvalidLines) ParseFile(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var invalidLines = new List<int>();

        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return (result, invalidLines); }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                invalidLines.Add(i + 1);
                continue;
            }

            var word = line[..eq].Trim().ToLowerInvariant();
            var ipa = line[(eq + 1)..].Trim();
            if (word.Length == 0 || ipa.Length == 0 || !IsValidIpa(ipa))
            {
                invalidLines.Add(i + 1);
                continue;
            }

            result[word] = ipa;
        }

        return (result, invalidLines);
    }

    private static bool IsValidIpa(string ipa)
    {
        foreach (var c in ipa)
            if (!KokoroVocab.SymbolToId.ContainsKey(c.ToString()))
                return false;

        return true;
    }
}
