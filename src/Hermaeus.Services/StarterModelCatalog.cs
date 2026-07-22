using Hermaeus.Core.Models;

namespace Hermaeus.Services;

/// <summary>
/// One downloadable starter chat model offered by the setup wizard for
/// users who do not already have a GGUF model on disk (docs/review
/// 02-onboarding-and-usability.md item 2.1). Every entry pins a SHA256
/// hash verified via <see cref="ModelDownloadService.VerifyHashAsync"/>
/// before the download is trusted, per the security-posture skill.
/// </summary>
public sealed record StarterModelEntry(
    string Id,
    string DisplayName,
    string FileName,
    string DownloadUrl,
    long SizeBytes,
    string Sha256);

public static class StarterModelCatalog
{
    // bartowski's Qwen2.5-Instruct GGUF quantizations: public, ungated
    // repositories, one consistent publisher/quantizer across all three
    // tiers. SHA256 values cross-checked against Hugging Face's LFS tree
    // API (`lfs.oid`) and the resolve endpoint's `X-Linked-ETag` header.
    public static readonly StarterModelEntry Small = new(
        "qwen2.5-3b-instruct-q4",
        "Qwen2.5 3B Instruct (Q4_K_M, ~1.9 GB)",
        "Qwen2.5-3B-Instruct-Q4_K_M.gguf",
        "https://huggingface.co/bartowski/Qwen2.5-3B-Instruct-GGUF/resolve/main/Qwen2.5-3B-Instruct-Q4_K_M.gguf",
        1_929_903_264,
        "9c9f56a391a3abbd5b89d0245bf6106081bcc3173119d4229235dd9d23253f94");

    public static readonly StarterModelEntry Medium = new(
        "qwen2.5-7b-instruct-q4",
        "Qwen2.5 7B Instruct (Q4_K_M, ~4.7 GB)",
        "Qwen2.5-7B-Instruct-Q4_K_M.gguf",
        "https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf",
        4_683_074_240,
        "65b8fcd92af6b4fefa935c625d1ac27ea29dcb6ee14589c55a8f115ceaaa1423");

    public static readonly StarterModelEntry Large = new(
        "qwen2.5-14b-instruct-q4",
        "Qwen2.5 14B Instruct (Q4_K_M, ~9.0 GB)",
        "Qwen2.5-14B-Instruct-Q4_K_M.gguf",
        "https://huggingface.co/bartowski/Qwen2.5-14B-Instruct-GGUF/resolve/main/Qwen2.5-14B-Instruct-Q4_K_M.gguf",
        8_988_110_976,
        "e47ad95dad6ff848b431053b375adb5d39321290ea2c638682577dafca87c008");

    public static readonly IReadOnlyList<StarterModelEntry> All = [Small, Medium, Large];

    /// <summary>
    /// Picks a tier from the best available GPU's VRAM. No GPU (or a probe
    /// that came back "unavailable") is treated as the smallest tier so the
    /// recommendation is always safe on CPU-only machines.
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
            _ => Small
        };
    }
}
