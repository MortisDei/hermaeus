namespace Aether.Core.Models;

public class ModelProfile
{
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public double? DefaultTemperature { get; set; }
    public int? DefaultContextSize { get; set; }
    public int? DefaultMaxTokens { get; set; }
    public string Backend { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public string Avatar { get; set; } = string.Empty;
}
