namespace Hermaeus.Core.Models;

public enum BenchmarkRunMode
{
    ColdWarm = 0,
    ColdOnly = 1,
    WarmOnly = 2
}

public enum BenchmarkPhase
{
    Cold = 0,
    Warm = 1
}

public sealed class BenchmarkRunMetadata
{
    public string AppVersion { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string ModelHash { get; set; } = string.Empty;
    public string Quantization { get; set; } = string.Empty;
    public string Backend { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public string RuntimeKind { get; set; } = string.Empty;
    public int? ContextSize { get; set; }
    public string PromptTemplate { get; set; } = string.Empty;
    public string SamplerSettings { get; set; } = string.Empty;
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? TopK { get; set; }
    public double? RepeatPenalty { get; set; }
    public int? Seed { get; set; }
    public int? GpuLayers { get; set; }
    public int? Threads { get; set; }
    public int? BatchSize { get; set; }
    public string EmbeddingModel { get; set; } = string.Empty;
    public bool? RerankerEnabled { get; set; }
    public string OS { get; set; } = string.Empty;
    public string CPU { get; set; } = string.Empty;
    public string RAM { get; set; } = string.Empty;
    public string GPU { get; set; } = string.Empty;
}
