using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hermaeus.Services;

// Isolated, inactive v3 transaction. SecretStore does not select this implementation.
internal sealed class FallbackVaultTransaction
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const string Failure = "The local vault operation is unavailable; explicit investigation or recovery is required.";
    private readonly string _vaultPath;
    private readonly string _directory;
    private readonly Func<string, byte[], string> _legacyDecoder;
    private readonly TimeSpan _lockTimeout;
    private readonly Action<string>? _fault;
    private readonly string? _legacyKeyPath;
    private readonly Action? _validateOperation;

    internal FallbackVaultTransaction(string vaultPath, string protectedDirectory,
        Func<string, byte[], string> strictLegacyDecoder, TimeSpan? lockTimeout = null,
        Action<string>? fault = null, string? legacyKeyPath = null, Action? validateOperation = null)
    {
        _vaultPath = Path.GetFullPath(vaultPath);
        _directory = Path.GetFullPath(protectedDirectory);
        var dataRoot = Path.GetDirectoryName(_vaultPath)
            ?? throw new ArgumentException("A vault file path is required.", nameof(vaultPath));
        var rootPrefix = Path.EndsInDirectorySeparator(dataRoot) ? dataRoot : dataRoot + Path.DirectorySeparatorChar;
        if (string.Equals(_directory, dataRoot, ModelPathSafety.LocalPathComparison)
            || _directory.StartsWith(rootPrefix, ModelPathSafety.LocalPathComparison))
            throw new ArgumentException("The protected vault directory must remain outside the Data Root.", nameof(protectedDirectory));
        _legacyDecoder = strictLegacyDecoder;
        _lockTimeout = lockTimeout ?? TimeSpan.FromSeconds(10);
        if (_lockTimeout <= TimeSpan.Zero || _lockTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(lockTimeout));
        _fault = fault;
        _legacyKeyPath = legacyKeyPath is null ? null : Path.GetFullPath(legacyKeyPath);
        _validateOperation = validateOperation;
    }

    internal sealed record RecoveryCandidate(string OriginalId, bool Available);
    internal sealed record RecoveryReview(string OriginalId, string TargetPath, string? TargetDigest,
        string SnapshotDigest, string LocatorDigest);
    internal sealed class RecoveryDiscoveryLimitException : IOException
    {
        internal RecoveryDiscoveryLimitException() : base("Recovery discovery requires an explicit original vault ID.") { }
    }

    internal Task<string?> ResolveAsync(string name, CancellationToken ct = default) => OwnedAsync(async () =>
    {
        name = FallbackVaultCodec.NormalizeName(name);
        FallbackVaultCodec.ValidateNormalizedName(name);
        var bytes = await ReadPrimaryAsync(ct);
        if (bytes is null) return null;
        byte[] key = [];
        try
        {
            var values = Parse(bytes);
            if (!values.TryGetValue(name, out var value)) return null;
            key = await ReadKeyAsync(values, false, ct);
            return Decode(name, value, key);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(bytes); }
    }, ct);

    internal Task MutateAsync(string name, string? newValue, CancellationToken ct = default) => OwnedAsync(async () =>
    {
        name = FallbackVaultCodec.NormalizeName(name);
        FallbackVaultCodec.ValidateNormalizedName(name);
        var original = await ReadPrimaryAsync(ct);
        byte[] key = [];
        try
        {
            var values = original is null ? new Dictionary<string, string>(StringComparer.Ordinal) : Parse(original);
            if (original is null && File.Exists(Path.Combine(_directory, "secrets.local.key"))) throw Unavailable();
            // A missing primary with a known recovery point is not an empty new vault.
            if (original is null && AnyRecoveryArtifacts())
                throw Unavailable();
            if (original is null && newValue is null) return true;
            key = await ReadKeyAsync(values, true, ct);
            var plain = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in values)
            {
                ct.ThrowIfCancellationRequested();
                plain.Add(pair.Key, Decode(pair.Key, pair.Value, key));
            }
            if (original is not null && values.Values.Any(v => FallbackVaultCodec.Classify(v) != FallbackVaultFormat.V3))
                await EnsureRecoveryAsync(original, key, ct);
            if (newValue is null) plain.Remove(name);
            else plain[name] = newValue;
            var encrypted = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in plain)
            {
                ct.ThrowIfCancellationRequested();
                encrypted.Add(pair.Key, FallbackVaultCodec.EncryptValue(pair.Key, pair.Value, key));
            }
            var output = JsonSerializer.SerializeToUtf8Bytes(encrypted);
            await PublishAsync(_vaultPath, output, true, "primary", ct);
            // The replacement is committed. Do not report cancellation as an untouched operation.
            var reopened = await ReadRequiredAsync(_vaultPath, CancellationToken.None);
            if (!reopened.AsSpan().SequenceEqual(output)) throw Unavailable();
            foreach (var pair in Parse(reopened)) _ = Decode(pair.Key, pair.Value, key);
            _fault?.Invoke("primary-reopened");
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (original is not null) CryptographicOperations.ZeroMemory(original);
        }
    }, ct);

    internal Task<IReadOnlyList<RecoveryCandidate>> DiscoverRecoveryAsync(CancellationToken ct = default) => OwnedAsync(async () =>
    {
        const int maximumCandidates = 64;
        var ids = new List<string>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.pre-v3.locator"))
        {
            var name = Path.GetFileName(path);
            var id = name[..^".pre-v3.locator".Length];
            if (!ValidId(id)) throw Unavailable();
            ids.Add(id);
            if (ids.Count > maximumCandidates) throw new RecoveryDiscoveryLimitException();
        }
        var result = new List<RecoveryCandidate>();
        foreach (var id in ids.Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var recovery = await ReadRecoveryAsync(id, ct);
                CryptographicOperations.ZeroMemory(recovery.Json);
                result.Add(new(id, true));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or FormatException or ArgumentException)
            { result.Add(new(id, false)); }
        }
        return (IReadOnlyList<RecoveryCandidate>)result;
    }, ct);

    internal Task<RecoveryReview> ReviewRecoveryAsync(string originalId, CancellationToken ct = default) => OwnedAsync(async () =>
    {
        var recovery = await ReadRecoveryAsync(originalId, ct);
        try
        {
            var target = await ReadPrimaryAsync(ct);
            try { return new RecoveryReview(originalId, _vaultPath, DigestOrNull(target), recovery.SnapshotDigest, recovery.LocatorDigest); }
            finally { if (target is not null) CryptographicOperations.ZeroMemory(target); }
        }
        finally { CryptographicOperations.ZeroMemory(recovery.Json); }
    }, ct);

    // This is an explicit internal recovery operation, never a read/mutation fallback.
    internal Task RestoreAsync(RecoveryReview review, CancellationToken ct = default) => OwnedAsync(async () =>
    {
        if (!string.Equals(review.TargetPath, _vaultPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw Unavailable();
        var recovery = await ReadRecoveryAsync(review.OriginalId, ct);
        try
        {
            var target = await ReadPrimaryAsync(ct);
            try
            {
                if (DigestOrNull(target) != review.TargetDigest || recovery.SnapshotDigest != review.SnapshotDigest
                    || recovery.LocatorDigest != review.LocatorDigest) throw Unavailable();
            }
            finally { if (target is not null) CryptographicOperations.ZeroMemory(target); }
            await PublishAsync(_vaultPath, recovery.Json, true, "restore", ct);
            var actual = await ReadRequiredAsync(_vaultPath, CancellationToken.None);
            try { if (!actual.AsSpan().SequenceEqual(recovery.Json)) throw Unavailable(); }
            finally { CryptographicOperations.ZeroMemory(actual); }
            return true;
        }
        finally { CryptographicOperations.ZeroMemory(recovery.Json); }
    }, ct);

    private string Decode(string name, string value, byte[] key) => FallbackVaultCodec.Classify(value) switch
    {
        FallbackVaultFormat.V3 => FallbackVaultCodec.DecryptValue(name, value, key),
        FallbackVaultFormat.LegacyV2 or FallbackVaultFormat.LegacyPlaintext => _legacyDecoder(value, key),
        _ => throw Unavailable()
    };

    private async Task EnsureRecoveryAsync(byte[] original, byte[] key, CancellationToken ct)
    {
        var id = Identity();
        var snapshotPath = Artifact(id, ".snapshot");
        var locatorPath = Artifact(id, ".locator");
        byte[] snapshot;
        if (File.Exists(snapshotPath))
        {
            (snapshot, _) = await ReadSnapshotFileAsync(snapshotPath, ct);
            var decoded = FallbackVaultCodec.DecryptSnapshot(Ascii(snapshot), key, Convert.FromHexString(id));
            try { if (!decoded.AsSpan().SequenceEqual(original)) throw Unavailable(); }
            finally { CryptographicOperations.ZeroMemory(decoded); }
        }
        else
        {
            if (File.Exists(locatorPath)) throw Unavailable();
            snapshot = Encoding.ASCII.GetBytes(FallbackVaultCodec.EncryptSnapshot(original, key, Convert.FromHexString(id)));
            await PublishAsync(snapshotPath, snapshot, false, "snapshot", ct);
        }
        if (!File.Exists(locatorPath))
        {
            var locator = Encoding.ASCII.GetBytes(FallbackVaultCodec.EncryptLocator(SHA256.HashData(snapshot), key, Convert.FromHexString(id)));
            await PublishAsync(locatorPath, locator, false, "locator", ct);
        }
        var verified = await ReadRecoveryAsync(id, ct);
        try { if (!verified.Json.AsSpan().SequenceEqual(original)) throw Unavailable(); }
        finally { CryptographicOperations.ZeroMemory(verified.Json); }
    }

    private sealed record RecoveryData(byte[] Json, string SnapshotDigest, string LocatorDigest);

    private async Task<RecoveryData> ReadRecoveryAsync(string id, CancellationToken ct)
    {
        if (!ValidId(id)) throw Unavailable();
        var key = await ReadExistingKeyAsync(ct);
        try
        {
            var locatorPath = Artifact(id, ".locator");
            ValidatePath(locatorPath);
            // v3: + padded Base64 of the fixed 76-byte locator envelope.
            if (new FileInfo(locatorPath).Length != 107) throw Unavailable();
            var locator = await ReadRequiredAsync(locatorPath, ct);
            var expectedHash = FallbackVaultCodec.DecryptLocator(Ascii(locator), key, Convert.FromHexString(id));
            var snapshotPath = Artifact(id, ".snapshot");
            var (snapshot, actualHash) = await ReadSnapshotFileAsync(snapshotPath, ct);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash)) throw Unavailable();
            var json = FallbackVaultCodec.DecryptSnapshot(Ascii(snapshot), key, Convert.FromHexString(id));
            try
            {
                foreach (var pair in Parse(json)) _ = Decode(pair.Key, pair.Value, key);
                return new(json, Convert.ToHexStringLower(actualHash), DigestOrNull(locator)!);
            }
            catch { CryptographicOperations.ZeroMemory(json); throw; }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private async Task<byte[]> ReadKeyAsync(Dictionary<string, string> values, bool create, CancellationToken ct)
    {
        var path = Path.Combine(_directory, "secrets.local.key");
        if (File.Exists(path)) return await ReadExistingKeyAsync(ct);
        if (values.Values.Any(v => FallbackVaultCodec.Classify(v) != FallbackVaultFormat.LegacyPlaintext)) throw Unavailable();
        if (create && (AnyRecoveryArtifacts() || (values.Count == 0 && File.Exists(_vaultPath)))) throw Unavailable();
        // Validate unencrypted legacy input before creating any key. Invalid JSON data
        // must not leave a replacement key behind merely because its prefix looked old.
        foreach (var pair in values) _ = _legacyDecoder(pair.Value, []);
        if (!create) return [];
        var key = RandomNumberGenerator.GetBytes(32);
        try { await PublishAsync(path, Encoding.ASCII.GetBytes(Convert.ToBase64String(key)), false, "key", ct); return key; }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
    }

    private static async Task<(byte[] Bytes, byte[] Digest)> ReadSnapshotFileAsync(string path, CancellationToken ct)
    {
        ValidatePath(path);
        ValidatePrivateFile(path);
        var length = new FileInfo(path).Length;
        // This codec requires a managed UTF-16 envelope string. Reject impossible
        // array/string representations before allocating; this is not a vault quota.
        if (length < 63 || (length - 3) % 4 != 0 || length > Array.MaxLength / sizeof(char)) throw Unavailable();
        byte[] digest;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            digest = await SHA256.HashDataAsync(stream, ct);
        var bytes = await ReadRequiredAsync(path, ct);
        if (bytes.LongLength != length || !CryptographicOperations.FixedTimeEquals(digest, SHA256.HashData(bytes))) throw Unavailable();
        return (bytes, digest);
    }

    private async Task<byte[]> ReadExistingKeyAsync(CancellationToken ct)
    {
        var bytes = await ReadRequiredAsync(Path.Combine(_directory, "secrets.local.key"), ct);
        try
        {
            var key = Convert.FromBase64String(Ascii(bytes));
            if (key.Length != 32) { CryptographicOperations.ZeroMemory(key); throw Unavailable(); }
            return key;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private async Task RelocateLegacyKeyAsync(CancellationToken ct)
    {
        var protectedPath = Path.Combine(_directory, "secrets.local.key");
        if (_legacyKeyPath is null || string.Equals(_legacyKeyPath, protectedPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
        ValidatePath(_legacyKeyPath);
        if (!File.Exists(_legacyKeyPath)) return;
        // A legacy-looking key alone cannot establish the identity of a lost
        // protected key once recovery history exists. Require explicit review.
        if (!File.Exists(protectedPath) && AnyRecoveryArtifacts()) throw Unavailable();
        var legacyBytes = await ReadRequiredAsync(_legacyKeyPath, ct);
        byte[] legacyKey = [];
        byte[] protectedKey = [];
        byte[] rechecked = [];
        try
        {
            legacyKey = Convert.FromBase64String(Ascii(legacyBytes));
            if (legacyKey.Length != 32) throw Unavailable();
            if (!File.Exists(protectedPath))
            {
                // Absence of a snapshot does not establish a pre-v3 vault. Fresh
                // v3 vaults have none, so never import a guessed legacy key into
                // their missing protected-key slot. Validate the entire legacy
                // dictionary with the candidate before publishing it anywhere.
                var primary = await ReadPrimaryAsync(ct);
                if (primary is not null)
                {
                    try
                    {
                        foreach (var pair in Parse(primary))
                        {
                            ct.ThrowIfCancellationRequested();
                            if (FallbackVaultCodec.Classify(pair.Value) is not (FallbackVaultFormat.LegacyPlaintext or FallbackVaultFormat.LegacyV2))
                                throw Unavailable();
                            _ = _legacyDecoder(pair.Value, legacyKey);
                        }
                    }
                    finally { CryptographicOperations.ZeroMemory(primary); }
                }
                await PublishAsync(protectedPath, legacyBytes, false, "legacy-key", ct);
            }
            protectedKey = await ReadExistingKeyAsync(CancellationToken.None);
            if (!CryptographicOperations.FixedTimeEquals(legacyKey, protectedKey)) throw Unavailable();
            _fault?.Invoke("legacy-key-verified");
            // Recheck the exact source before deleting only this captured legacy key.
            rechecked = await ReadRequiredAsync(_legacyKeyPath, CancellationToken.None);
            if (!legacyBytes.AsSpan().SequenceEqual(rechecked)) throw Unavailable();
            ValidatePath(_legacyKeyPath);
            File.Delete(_legacyKeyPath);
            _fault?.Invoke("legacy-key-removed");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(legacyBytes);
            CryptographicOperations.ZeroMemory(legacyKey);
            CryptographicOperations.ZeroMemory(protectedKey);
            CryptographicOperations.ZeroMemory(rechecked);
        }
    }

    private async Task<byte[]?> ReadPrimaryAsync(CancellationToken ct)
    {
        ValidatePath(_vaultPath);
        return File.Exists(_vaultPath) ? await ReadRequiredAsync(_vaultPath, ct) : null;
    }

    private static async Task<byte[]> ReadRequiredAsync(string path, CancellationToken ct)
    {
        ValidatePath(path);
        ValidatePrivateFile(path);
        return await File.ReadAllBytesAsync(path, ct);
    }

    private static Dictionary<string, string> Parse(byte[] json)
    {
        _ = Utf8.GetCharCount(json);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw Unavailable();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            FallbackVaultCodec.ValidateNormalizedName(property.Name);
            if (property.Value.ValueKind != JsonValueKind.String || !result.TryAdd(property.Name, property.Value.GetString()!))
                throw Unavailable();
        }
        return result;
    }

    private async Task PublishAsync(string path, byte[] bytes, bool replace, string phase, CancellationToken ct)
    {
        ValidatePath(path);
        if (File.Exists(path)) ValidatePrivateFile(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        ValidatePath(directory);
        var temp = Path.Combine(directory, ".vault-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temp, options))
            {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }
            _fault?.Invoke(phase + "-flushed");
            ct.ThrowIfCancellationRequested();
            ValidatePath(path);
            if (phase is "primary" or "restore") _validateOperation?.Invoke();
            File.Move(temp, path, replace);
            _fault?.Invoke(phase + "-replaced");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private async Task<T> OwnedAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        var acquired = false;
        try
        {
            var clock = Stopwatch.StartNew();
            acquired = await Gate.WaitAsync(_lockTimeout, ct);
            if (!acquired) throw Unavailable();
            ValidatePath(_directory);
            if (!Directory.Exists(_directory))
            {
                if (OperatingSystem.IsWindows()) Directory.CreateDirectory(_directory);
                else Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            ValidatePath(_directory);
            if (!OperatingSystem.IsWindows()
                && (File.GetUnixFileMode(_directory) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw Unavailable();
            var lockPath = Path.Combine(_directory, "secrets.local.lock");
            FileStream? ownership = null;
            while (ownership is null)
            {
                ct.ThrowIfCancellationRequested();
                if (clock.Elapsed >= _lockTimeout) throw Unavailable();
                ValidatePath(lockPath);
                if (File.Exists(lockPath)) ValidatePrivateFile(lockPath);
                try
                {
                    var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
                    if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    ownership = new FileStream(lockPath, options);
                }
                catch (IOException)
                {
                    if (clock.Elapsed >= _lockTimeout) throw Unavailable();
                    await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
                }
            }
            await using (ownership)
            {
                _validateOperation?.Invoke();
                await RelocateLegacyKeyAsync(ct);
                return await operation();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (RecoveryDiscoveryLimitException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or FormatException or ArgumentException or OverflowException or InvalidOperationException)
        { throw Unavailable(); }
        finally { if (acquired) Gate.Release(); }
    }

    private string Identity()
    {
        var identity = OperatingSystem.IsWindows() ? _vaultPath.ToUpperInvariant() : _vaultPath;
        return Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(identity)));
    }

    private string Artifact(string id, string suffix)
    {
        if (!ValidId(id)) throw Unavailable();
        return Path.Combine(_directory, id + ".pre-v3" + suffix);
    }

    private bool AnyRecoveryArtifacts() => Directory.EnumerateFileSystemEntries(_directory, "*.pre-v3.*").Any();

    private static bool ValidId(string id) => id.Length == 64 && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string? DigestOrNull(byte[]? bytes) => bytes is null ? null : Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Ascii(byte[] bytes)
    {
        if (bytes.Any(b => b > 127)) throw Unavailable();
        return Encoding.ASCII.GetString(bytes);
    }

    private static void ValidatePath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            // GetAttributes also detects dangling links, which Exists intentionally follows.
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw Unavailable(); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void ValidatePrivateFile(string path)
    {
        if (!OperatingSystem.IsWindows()
            && (File.GetUnixFileMode(path) & ~(UnixFileMode.UserRead | UnixFileMode.UserWrite)) != 0)
            throw Unavailable();
    }

    private static IOException Unavailable() => new(Failure);
}
