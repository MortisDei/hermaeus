namespace Hermaeus.Rag.Models;

public enum IngestDuplicatePolicy
{
    SkipIfUnchanged,
    Replace,
    ReportOnly
}

public class IngestOptions
{
    public bool DryRun { get; set; } = false;
    public IngestDuplicatePolicy DuplicatePolicy { get; set; } = IngestDuplicatePolicy.Replace;
}

public enum DocumentIngestStatus
{
    Added,
    Replaced,
    SkippedUnchanged,
    ReportOnly,
    Error
}

public class DocumentIngestReport
{
    public string Path { get; init; } = string.Empty;
    public DocumentIngestStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
}

public class IngestReport
{
    public List<DocumentIngestReport> Documents { get; } = new();

    // Aggregate health information about the ingest run (formerly emitted as a __health__ sentinel document)
    public RagIngestHealth? Health { get; set; }

    public int Added => Documents.Count(d => d.Status == DocumentIngestStatus.Added);
    public int Replaced => Documents.Count(d => d.Status == DocumentIngestStatus.Replaced);
    public int Skipped => Documents.Count(d => d.Status == DocumentIngestStatus.SkippedUnchanged);
    public int ReportOnly => Documents.Count(d => d.Status == DocumentIngestStatus.ReportOnly);
    public int Errors => Documents.Count(d => d.Status == DocumentIngestStatus.Error);

    public string Summary() => $"Added: {Added}, Replaced: {Replaced}, Skipped: {Skipped}, ReportOnly: {ReportOnly}, Errors: {Errors}";
}
