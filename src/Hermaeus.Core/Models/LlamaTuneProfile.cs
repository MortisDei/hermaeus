namespace Hermaeus.Core.Models;

public sealed class LlamaTuneProfile
{
    public string ModelPath { get; set; } = string.Empty;
    public long ModelSizeBytes { get; set; }
    public DateTime ModelModifiedAtUtc { get; set; }
    public int GpuLayers { get; set; }
    public int? TotalLayers { get; set; }
    public int Threads { get; set; }
    public int ContextSize { get; set; }
    public string ExtraArgs { get; set; } = string.Empty;
    public string LlamaServerVersion { get; set; } = string.Empty;
    public DateTime TunedAtUtc { get; set; } = DateTime.UtcNow;

    public string ModelFileName => string.IsNullOrWhiteSpace(ModelPath)
        ? string.Empty
        : Path.GetFileName(ModelPath);
}
