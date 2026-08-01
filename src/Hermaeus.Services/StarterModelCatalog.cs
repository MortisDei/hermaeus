using Hermaeus.Core.Models;

namespace Hermaeus.Services;

/// <summary>
/// One downloadable starter chat model offered by the setup wizard for
/// users who do not already have a GGUF model on disk (docs/review
/// 02-onboarding-and-usability.md item 2.1). Every entry pins a SHA256
/// hash verified via <see cref="ModelDownloadService.VerifyHashAsync"/>
/// before the download is trusted, per the security-posture skill.
/// </summary>
/// <param name="License">
/// The base model's licence, short form, shown in the wizard before the user
/// downloads anything. Not every model here is permissively licensed and the
/// differences are real: one is research and non-commercial only, and one
/// carries an attribution requirement. A user choosing a model deserves to
/// know which terms they are taking on at the moment they choose, not after.
/// </param>
/// <param name="LicenseUrl">Where to read the licence in full.</param>
/// <param name="LicenseNote">
/// The obligation or restriction in one line; empty when there is nothing to
/// say beyond the licence name. A pointer, never a legal opinion.
/// </param>
public sealed record StarterModelEntry(
    string Id,
    string DisplayName,
    string FileName,
    string DownloadUrl,
    long SizeBytes,
    string Sha256,
    string License = "",
    string LicenseUrl = "",
    string LicenseNote = "")
{
    /// <summary>"MIT" or "Llama 3.2 Community License - attribution required...".</summary>
    public string LicenseDisplay => string.IsNullOrEmpty(LicenseNote)
        ? License
        : $"{License} - {LicenseNote}";

    public bool HasLicense => !string.IsNullOrEmpty(License);
}

public static class StarterModelCatalog
{
    // Quantized GGUF builds from public, ungated repositories. The SHA256
    // values are the Hugging Face LFS object ids (`lfs.oid` from the tree
    // API), which is the SHA256 of the file contents; re-checked 2026-08-01.
    //
    // Publishers: bartowski for the Qwen tiers (unchanged since r8, one
    // consistent quantizer across all three), unsloth for the three added in
    // 0.36.0-alpha. Both are widely used and ungated, and publish a matching
    // base_model link on every repo.

    // ── Qwen2.5, the original three tiers ────────────────────────────────────

    public static readonly StarterModelEntry Small = new(
        "qwen2.5-3b-instruct-q4",
        "Qwen2.5 3B Instruct (Q4_K_M, ~1.9 GB)",
        "Qwen2.5-3B-Instruct-Q4_K_M.gguf",
        "https://huggingface.co/bartowski/Qwen2.5-3B-Instruct-GGUF/resolve/main/Qwen2.5-3B-Instruct-Q4_K_M.gguf",
        1_929_903_264,
        "9c9f56a391a3abbd5b89d0245bf6106081bcc3173119d4229235dd9d23253f94",
        // The one entry in this catalog that is NOT permissively licensed. Its
        // 7B and 14B siblings are Apache-2.0; this size alone is not.
        License: "Qwen Research License",
        LicenseUrl: "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct/blob/main/LICENSE",
        LicenseNote: "research and non-commercial use only");

    public static readonly StarterModelEntry Medium = new(
        "qwen2.5-7b-instruct-q4",
        "Qwen2.5 7B Instruct (Q4_K_M, ~4.7 GB)",
        "Qwen2.5-7B-Instruct-Q4_K_M.gguf",
        "https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf",
        4_683_074_240,
        "65b8fcd92af6b4fefa935c625d1ac27ea29dcb6ee14589c55a8f115ceaaa1423",
        License: "Apache-2.0",
        LicenseUrl: "https://huggingface.co/Qwen/Qwen2.5-7B-Instruct");

    public static readonly StarterModelEntry Large = new(
        "qwen2.5-14b-instruct-q4",
        "Qwen2.5 14B Instruct (Q4_K_M, ~9.0 GB)",
        "Qwen2.5-14B-Instruct-Q4_K_M.gguf",
        "https://huggingface.co/bartowski/Qwen2.5-14B-Instruct-GGUF/resolve/main/Qwen2.5-14B-Instruct-Q4_K_M.gguf",
        8_988_110_976,
        "e47ad95dad6ff848b431053b375adb5d39321290ea2c638682577dafca87c008",
        License: "Apache-2.0",
        LicenseUrl: "https://huggingface.co/Qwen/Qwen2.5-14B-Instruct");

    // ── Alternatives, so the choice is the user's ────────────────────────────

    public static readonly StarterModelEntry Llama32_3B = new(
        "llama-3.2-3b-instruct-q4",
        "Llama 3.2 3B Instruct (Q4_K_M, ~2.0 GB)",
        "Llama-3.2-3B-Instruct-Q4_K_M.gguf",
        "https://huggingface.co/unsloth/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-Q4_K_M.gguf",
        2_019_377_600,
        "6c99cc00ae910f6a532a80022cb4bc1939094527a089c29294b841c0bd87f74d",
        License: "Llama 3.2 Community License",
        LicenseUrl: "https://huggingface.co/meta-llama/Llama-3.2-3B-Instruct/blob/main/LICENSE.txt",
        LicenseNote: "acceptable-use policy, and attribution if you build on it");

    public static readonly StarterModelEntry Phi4Mini = new(
        "phi-4-mini-instruct-q4",
        "Phi-4 mini Instruct (Q4_K_M, ~2.5 GB)",
        "Phi-4-mini-instruct-Q4_K_M.gguf",
        "https://huggingface.co/unsloth/Phi-4-mini-instruct-GGUF/resolve/main/Phi-4-mini-instruct-Q4_K_M.gguf",
        2_491_874_272,
        "88c00229914083cd112853aab84ed51b87bdf6b9ce42f532d8c85c7c63b1730a",
        License: "MIT",
        LicenseUrl: "https://huggingface.co/microsoft/Phi-4-mini-instruct");

    public static readonly StarterModelEntry Gemma4_E4B = new(
        "gemma-4-e4b-it-q4",
        "Gemma 4 E4B Instruct (Q4_K_M, ~5.0 GB)",
        "gemma-4-E4B-it-Q4_K_M.gguf",
        "https://huggingface.co/unsloth/gemma-4-E4B-it-GGUF/resolve/main/gemma-4-E4B-it-Q4_K_M.gguf",
        4_977_171_584,
        "85a896a047553e842f25297ee5b031d64ff30147d9c4af17b1e4b394cd1fab87",
        License: "Apache-2.0",
        LicenseUrl: "https://ai.google.dev/gemma/docs/gemma_4_license",
        LicenseNote: "plus Google's Gemma prohibited-use policy");

    /// <summary>
    /// Everything the wizard offers, roughly smallest first. <see cref="Recommend"/>
    /// picks a default from the machine's VRAM; the user picks from this list.
    /// </summary>
    public static readonly IReadOnlyList<StarterModelEntry> All =
        [Small, Llama32_3B, Phi4Mini, Medium, Gemma4_E4B, Large];

    /// <summary>
    /// Picks a tier from the best available GPU's VRAM. No GPU (or a probe
    /// that came back "unavailable") is treated as the smallest tier so the
    /// recommendation is always safe on CPU-only machines.
    ///
    /// The low tier is <see cref="Phi4Mini"/> (MIT) rather than Qwen2.5 3B,
    /// which is research and non-commercial only. Recommending a restricted
    /// model by default to exactly the users least likely to read a licence
    /// was the wrong default; Qwen2.5 3B is still offered, with its terms
    /// stated, for anyone who wants it.
    /// </summary>
    public static StarterModelEntry Recommend(SystemSnapshot snapshot)
    {
        var vramBytes = snapshot.Gpus
            .Where(g => !string.Equals(g.Status, "unavailable", StringComparison.OrdinalIgnoreCase))
            .Select(g => g.MemoryTotalBytes ?? 0)
            .DefaultIfEmpty(0)
            .Max();

        const long twelveGb = 12L * 1024 * 1024 * 1024;
        const long sixGb = 6L * 1024 * 1024 * 1024;

        return vramBytes switch
        {
            >= twelveGb => Large,
            >= sixGb => Medium,
            _ => Phi4Mini
        };
    }
}
