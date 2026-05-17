namespace Aether.Services;

public sealed record EmbeddingModelDownloadSpec(
    string ModelName,
    string FileName,
    string Url,
    string Sha256);
