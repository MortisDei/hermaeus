namespace Hermaeus.Core.Models;

public class ModelProfile
{
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public double? DefaultTemperature { get; set; }
    public int? DefaultContextSize { get; set; }
    public string? DefaultKvCacheType { get; set; }
    public bool? DefaultPreserveReasoning { get; set; }
    public int? DefaultMaxTokens { get; set; }
    public double? DefaultTopP { get; set; }
    public int? DefaultTopK { get; set; }
    public double? DefaultMinP { get; set; }
    public double? DefaultRepeatPenalty { get; set; }
    public double? DefaultFrequencyPenalty { get; set; }
    public double? DefaultPresencePenalty { get; set; }
    public string Backend { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public string Avatar { get; set; } = string.Empty;
}
