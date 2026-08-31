namespace Hermaeus.Core.Models;

public sealed class LlamaTuneProfile
{
    public string ModelPath { get; set; } = string.Empty;
    public long ModelSizeBytes { get; set; }
    public DateTime ModelModifiedAtUtc { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int GpuLayers { get; set; }
    public GpuPlacementIntent? GpuPlacement { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("GpuLayers")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? LegacyGpuLayersJson
    {
        get => GpuPlacement is null ? GpuLayers : null;
        set
        {
            if (value is int legacy)
                GpuLayers = legacy;
        }
    }
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
