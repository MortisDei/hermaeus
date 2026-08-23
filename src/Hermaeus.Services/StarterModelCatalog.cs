using Hermaeus.Core.Models;

namespace Hermaeus.Services;

/// <summary>
/// One verified starter chat model offered by onboarding. The publisher's
/// licence and provenance are shown before download and every file is checked
/// by <see cref="ModelDownloadService.VerifyHashAsync"/> before adoption.
/// </summary>
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
    public string LicenseDisplay => string.IsNullOrEmpty(LicenseNote)
        ? License
        : $"{License} - {LicenseNote}";

    public bool HasLicense => !string.IsNullOrEmpty(License);
}

public static class StarterModelCatalog
{
    // Provenance is deliberately narrow: Qwen's own GGUF repositories for
    // Qwen3, Unsloth's official-base-model QAT conversions for Gemma 4, and
    // Unsloth's Phi-4 mini conversion. All are ungated and document llama.cpp
    // use. Sizes and Hugging Face LFS/Xet SHA256 values were checked 2026-08-23.

    public static readonly StarterModelEntry Gemma4_E2B = new(
        "gemma-4-e2b-it-qat-ud-q4",
        "Gemma 4 E2B IT QAT (UD-Q4_K_XL, ~2.6 GB)",
        "gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf",
        "https://huggingface.co/unsloth/gemma-4-E2B-it-qat-GGUF/resolve/main/gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf",
        2_620_370_976,
        "e531007218dfab990486a5de7676a6932d6ea8dea233d1f698d7c21cf8a16889",
        License: "Apache-2.0",
        LicenseUrl: "https://huggingface.co/unsloth/gemma-4-E2B-it-qat-GGUF");

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
        "gemma-4-e4b-it-qat-ud-q4",
        "Gemma 4 E4B IT QAT (UD-Q4_K_XL, ~4.2 GB)",
        "gemma-4-E4B-it-qat-UD-Q4_K_XL.gguf",
        "https://huggingface.co/unsloth/gemma-4-E4B-it-qat-GGUF/resolve/main/gemma-4-E4B-it-qat-UD-Q4_K_XL.gguf",
        4_215_695_776,
        "df0fd4ee07072c607c29a0a1cb4f98918426cca12f45a2776bdd6ee6d09a4de3",
        License: "Apache-2.0",
        LicenseUrl: "https://huggingface.co/unsloth/gemma-4-E4B-it-qat-GGUF");

    public static readonly StarterModelEntry Qwen3_8B = new(
        "qwen3-8b-q4",
        "Qwen3 8B (Q4_K_M, ~5.0 GB)",
        "Qwen3-8B-Q4_K_M.gguf",
        "https://huggingface.co/Qwen/Qwen3-8B-GGUF/resolve/main/Qwen3-8B-Q4_K_M.gguf",
        5_027_783_488,
        "d98cdcbd03e17ce47681435b5150e34c1417f50b5c0019dd560e4882c5745785",
        License: "Apache-2.0",
        LicenseUrl: "https://huggingface.co/Qwen/Qwen3-8B-GGUF");

    public static readonly StarterModelEntry Qwen3_14B = new(
        "qwen3-14b-q4",
        "Qwen3 14B (Q4_K_M, ~9.0 GB)",
        "Qwen3-14B-Q4_K_M.gguf",
        "https://huggingface.co/Qwen/Qwen3-14B-GGUF/resolve/main/Qwen3-14B-Q4_K_M.gguf",
        9_001_752_960,
        "500a8806e85ee9c83f3ae08420295592451379b4f8cf2d0f41c15dffeb6b81f0",
        License: "Apache-2.0",
        LicenseUrl: "https://huggingface.co/Qwen/Qwen3-14B-GGUF");

    // Stable tier aliases used by onboarding and its tests.
    public static StarterModelEntry Small => Phi4Mini;
    public static StarterModelEntry Medium => Gemma4_E4B;
    public static StarterModelEntry Large => Qwen3_14B;

    public static readonly IReadOnlyList<StarterModelEntry> All =
        [Phi4Mini, Gemma4_E2B, Gemma4_E4B, Qwen3_8B, Qwen3_14B];

    /// <summary>
    /// Picks a conservative tier from the best available GPU's VRAM. Gemma 4
    /// E4B QAT leaves room for context and runtime overhead on common 6-8 GB
    /// cards. Qwen3 8B remains an explicit alternative rather than the default.
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
