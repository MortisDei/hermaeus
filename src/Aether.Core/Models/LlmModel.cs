namespace Aether.Core.Models;

public class LlmModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ProviderTag { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string ProfileDisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public double? DefaultTemperature { get; set; }
    public int? DefaultContextSize { get; set; }
    public int? DefaultMaxTokens { get; set; }
    public double? DefaultTopP { get; set; }
    public int? DefaultTopK { get; set; }
    public double? DefaultMinP { get; set; }
    public double? DefaultRepeatPenalty { get; set; }
    public double? DefaultFrequencyPenalty { get; set; }
    public double? DefaultPresencePenalty { get; set; }
    /// <summary>Context length read live from the provider (llama.cpp /props, Ollama /api/show), if available.</summary>
    public int? ProbedContextLength { get; set; }
    public bool IsVisible { get; set; } = true;
    public string Avatar { get; set; } = string.Empty;

    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(ProfileDisplayName) ? Name : ProfileDisplayName.Trim();
            return string.IsNullOrEmpty(Provider) ? name : $"{name}  [{Provider}]";
        }
    }

    public string TagsDisplay => string.Join(", ", Tags);

    public string SizeDisplay => SizeBytes switch
    {
        > 1_073_741_824 => $"{SizeBytes / 1_073_741_824.0:F1} GB",
        > 1_048_576      => $"{SizeBytes / 1_048_576.0:F0} MB",
        _                => string.Empty
    };
}
