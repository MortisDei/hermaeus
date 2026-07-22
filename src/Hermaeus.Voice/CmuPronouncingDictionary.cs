using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Hermaeus.Voice;

/// <summary>
/// Lazily loads the embedded CMU Pronouncing Dictionary (cmudict, BSD-style
/// CMU license; see THIRD-PARTY-NOTICES.md) and converts each entry's
/// ARPABET pronunciation to IPA using <see cref="ArpabetIpaMap"/>. This is
/// the primary word-to-pronunciation source for <see cref="KokoroPhonemizer"/>;
/// the rule-based letter fallback only runs for words this dictionary
/// (and the user lexicon) do not have.
/// </summary>
internal static class CmuPronouncingDictionary
{
    private const string ResourceName = "Hermaeus.Voice.Assets.cmudict.txt.gz";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Entries =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static bool TryGetIpa(string word, out string ipa) =>
        Entries.Value.TryGetValue(word, out ipa!);

    private static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(130_000, StringComparer.Ordinal);
        var assembly = Assembly.GetExecutingAssembly();
        using var resourceStream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        using var gzip = new GZipStream(resourceStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var spaceIdx = line.IndexOf(' ');
            if (spaceIdx <= 0) continue;

            var word = line[..spaceIdx];
            var phones = line[(spaceIdx + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            map[word] = ConvertArpabetToIpa(phones);
        }

        return map;
    }

    internal static string ConvertArpabetToIpa(IReadOnlyList<string> phones)
    {
        var sb = new StringBuilder();
        foreach (var phone in phones)
        {
            var (letters, stress) = SplitStress(phone);

            var ipa = letters == "AH"
                ? (stress == '0' ? ArpabetIpaMap.UnstressedAh : ArpabetIpaMap.StressedAh)
                : ArpabetIpaMap.Map.GetValueOrDefault(letters, string.Empty);

            if (ipa.Length == 0)
                continue;

            if (stress == '1')
                sb.Append('ˈ');
            else if (stress == '2')
                sb.Append('ˌ');

            sb.Append(ipa);
        }

        return sb.ToString();
    }

    private static (string Letters, char Stress) SplitStress(string phone)
    {
        var last = phone[^1];
        if (last is >= '0' and <= '2')
            return (phone[..^1], last);
        return (phone, '\0');
    }
}
