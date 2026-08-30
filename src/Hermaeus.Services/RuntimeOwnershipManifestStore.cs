using System.Text.Json;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

internal enum RuntimeOwnershipState
{
    Missing,
    Known,
    Unknown
}

internal sealed record RuntimeOwner(
    string OwnershipId,
    string RunId,
    int ProcessId,
    DateTime StartedAtUtc,
    string ExecutableSha256,
    int Port,
    DateTime RecordedAtUtc);

internal sealed record RuntimeOwnershipReadResult(
    RuntimeOwnershipState State,
    IReadOnlyList<RuntimeOwner> Owners)
{
    public static RuntimeOwnershipReadResult Missing { get; } = new(RuntimeOwnershipState.Missing, []);
}

internal sealed class RuntimeOwnershipUnknownException : InvalidOperationException
{
    public RuntimeOwnershipUnknownException()
        : base("Lab runtime ownership evidence is unreadable; ownership mutation was refused.")
    {
    }
}

/// <summary>
/// Reads and mutates the isolated Lab runtime ownership manifest without
/// collapsing unreadable evidence into an empty owner list.
/// </summary>
internal sealed class RuntimeOwnershipManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly Action? _onUnknown;

    public RuntimeOwnershipManifestStore(string path, Action? onUnknown = null)
    {
        _path = path;
        _onUnknown = onUnknown;
    }

    public async Task<RuntimeOwnershipReadResult> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var owners = await JsonSerializer.DeserializeAsync<List<RuntimeOwner>>(stream, JsonOptions, ct);
            return owners is null
                ? Unknown()
                : new RuntimeOwnershipReadResult(RuntimeOwnershipState.Known, owners);
        }
        catch (FileNotFoundException)
        {
            return RuntimeOwnershipReadResult.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return RuntimeOwnershipReadResult.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return Unknown();
        }
    }

    public async Task AddAsync(RuntimeOwner owner, CancellationToken ct = default)
    {
        var result = await ReadAsync(ct);
        EnsureMutable(result);
        var owners = result.Owners.ToList();
        owners.RemoveAll(item => item.OwnershipId == owner.OwnershipId);
        owners.Add(owner);
        await WriteAsync(owners.TakeLast(32).ToArray(), ct);
    }

    public async Task RemoveAsync(string ownershipId, CancellationToken ct = default)
    {
        var result = await ReadAsync(ct);
        EnsureMutable(result);
        var owners = result.Owners.ToList();
        owners.RemoveAll(item => item.OwnershipId == ownershipId);
        await WriteAsync(owners, ct);
    }

    public async Task WriteAsync(IReadOnlyCollection<RuntimeOwner> owners, CancellationToken ct = default)
    {
        if (owners.Count == 0)
        {
            try { File.Delete(_path); }
            catch (DirectoryNotFoundException) { }
            return;
        }

        await AtomicFile.WriteAllTextAsync(_path, JsonSerializer.Serialize(owners, JsonOptions), ct);
    }

    private RuntimeOwnershipReadResult Unknown()
    {
        _onUnknown?.Invoke();
        return new RuntimeOwnershipReadResult(RuntimeOwnershipState.Unknown, []);
    }

    private static void EnsureMutable(RuntimeOwnershipReadResult result)
    {
        if (result.State == RuntimeOwnershipState.Unknown)
            throw new RuntimeOwnershipUnknownException();
    }
}
