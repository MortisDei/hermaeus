namespace Aether.Core.Models;

public class LlmModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime? ModifiedAt { get; set; }

    public string DisplayName => string.IsNullOrEmpty(Provider) ? Name : $"{Name}  [{Provider}]";

    public string SizeDisplay => SizeBytes switch
    {
        > 1_073_741_824 => $"{SizeBytes / 1_073_741_824.0:F1} GB",
        > 1_048_576      => $"{SizeBytes / 1_048_576.0:F0} MB",
        _                => string.Empty
    };
}
