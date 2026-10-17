using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class SecretStore : ISecretStore
{
    private readonly ISettingsService _settings;
    private readonly ISecretBackend _osBackend;
    private readonly IRuntimeLogService? _log;
    private readonly string _fallbackKeyDirectory;
    private readonly bool _authenticatedFallback;
    private const string Prefix = "secret:";
    private static StringComparison LocalPathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private const string EncryptedPrefix = "v2:";
    private const int SaltBytes = 16;
    private const int AesIvBytes = 16;
    private const int KeyIterations = 100_000;

    public SecretStore(ISettingsService settings, IRuntimeLogService? log = null)
        : this(settings, log, null)
    {
    }

    internal SecretStore(ISettingsService settings, IRuntimeLogService? log, string? fallbackKeyDirectory)
        : this(settings, log, fallbackKeyDirectory, false)
    {
    }

    // Isolated verification seam. No production setting or environment flag activates v3.
    internal SecretStore(ISettingsService settings, IRuntimeLogService? log, string? fallbackKeyDirectory, bool authenticatedFallback)
    {
        _settings = settings;
        _osBackend = CreateOsBackend();
        _log = log;
        _fallbackKeyDirectory = string.IsNullOrWhiteSpace(fallbackKeyDirectory)
            ? ResolveFallbackKeyDirectory()
            : Path.GetFullPath(fallbackKeyDirectory);
        _authenticatedFallback = authenticatedFallback;
        if (!authenticatedFallback) TryMigrateLegacyKey();
    }

    public bool IsReference(string value) => value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public async Task<string> StoreAsync(string name, string secret, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secret)) return string.Empty;
        if (IsReference(secret)) return secret;

        name = NormalizeName(name);
        if (UseOsKeychain() && await _osBackend.StoreAsync(name, secret, ct))
        {
            await RemoveLocalAsync(name, ct);
            return $"{Prefix}{name}";
        }

        if (_authenticatedFallback)
        {
            await CreateAuthenticatedTransaction().MutateAsync(name, secret, ct);
            return $"{Prefix}{name}";
        }
        var all = await LoadAsync(ct);
        ValidateLegacyMutation(all);
        all[name] = EncryptSecret(secret);
        var path = PathForStore();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WriteTextAtomicAsync(path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }), ct);
        TryRestrictPermissions(path);
        return $"{Prefix}{name}";
    }

    public async Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default)
    {
        if (!IsReference(valueOrReference)) return valueOrReference;
        var key = NormalizeName(valueOrReference[Prefix.Length..]);
        if (UseOsKeychain())
        {
            var secret = await _osBackend.ResolveAsync(key, ct);
            if (!string.IsNullOrEmpty(secret))
                return secret;
        }

        if (_authenticatedFallback)
        {
            try { return await CreateAuthenticatedTransaction().ResolveAsync(key, ct) ?? string.Empty; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or FormatException or ArgumentException)
            {
                LogUnavailableSecret();
                return string.Empty;
            }
        }
        var all = await LoadAsync(ct);
        if (!all.TryGetValue(key, out var encrypted)) return string.Empty;
        return DecryptSecret(encrypted);
    }

    public async Task<string> BackendLabelAsync(CancellationToken ct = default)
    {
        if (!UseOsKeychain()) return "Local fallback file";
        return await _osBackend.IsAvailableAsync(ct) ? _osBackend.Label : "Local fallback file";
    }

    private async Task RemoveLocalAsync(string name, CancellationToken ct)
    {
        if (_authenticatedFallback)
        {
            await CreateAuthenticatedTransaction().MutateAsync(name, null, ct);
            return;
        }
        var path = PathForStore();
        if (!File.Exists(path)) return;
        var all = await LoadAsync(ct);
        ValidateLegacyMutation(all);
        if (!all.Remove(name)) return;
        if (all.Count == 0)
        {
            File.Delete(path);
            return;
        }

        await WriteTextAtomicAsync(path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }), ct);
        TryRestrictPermissions(path);
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        var path = PathForStore();
        if (!File.Exists(path)) return [];
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
    }

    private string PathForStore()
    {
        var root = SettingsService.ResolveDataRoot(_settings.Settings);
        return Path.Combine(root, "secrets.local.json");
    }

    private FallbackVaultTransaction CreateAuthenticatedTransaction()
    {
        var capturedRoot = Path.GetFullPath(SettingsService.ResolveDataRoot(_settings.Settings));
        void ValidateOperation()
        {
            var data = _settings.Settings.DataManagement;
            if (!string.IsNullOrWhiteSpace(data.PendingDataRootDirectory)
                || !string.IsNullOrWhiteSpace(data.PendingDataRootMigrationPlan)
                || !string.Equals(capturedRoot, Path.GetFullPath(SettingsService.ResolveDataRoot(_settings.Settings)),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("The local vault is unavailable during a Data Root transition.");
        }
        ValidateOperation();
        return new FallbackVaultTransaction(Path.Combine(capturedRoot, "secrets.local.json"), _fallbackKeyDirectory,
            DecodeLegacyValue, legacyKeyPath: Path.Combine(capturedRoot, "secrets.local.key"), validateOperation: ValidateOperation);
    }

    private void ValidateLegacyMutation(Dictionary<string, string> values)
    {
        if (values.Values.Any(value => FallbackVaultCodec.Classify(value) is FallbackVaultFormat.V3 or FallbackVaultFormat.UnsupportedVersion))
            throw new CryptographicException("The local vault requires an explicitly accepted format activation.");
        byte[] key = [];
        try
        {
            if (values.Values.Any(value => FallbackVaultCodec.Classify(value) == FallbackVaultFormat.LegacyV2))
                key = ReadLocalKeyMaterial(KeyPath());
            foreach (var value in values.Values) _ = DecodeLegacyValue(value, key);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static ISecretBackend CreateOsBackend()
    {
        if (OperatingSystem.IsWindows()) return new WindowsCredentialBackend();
        if (OperatingSystem.IsMacOS()) return new MacOsKeychainBackend();
        if (OperatingSystem.IsLinux()) return new LinuxSecretServiceBackend();
        return NullSecretBackend.Instance;
    }

    private static bool UseOsKeychain() =>
        !string.Equals(Environment.GetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN"), "1", StringComparison.Ordinal);

    private static string NormalizeName(string name) => string.IsNullOrWhiteSpace(name)
        ? "default"
        : name.Trim();

    private static void TryRestrictPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { }
    }

    private static async Task WriteTextAtomicAsync(string path, string content, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content, ct);
            TryRestrictPermissions(temp);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private string KeyPath()
    {
        return Path.Combine(_fallbackKeyDirectory, "secrets.local.key");
    }

    private string LegacyKeyPath() =>
        Path.Combine(SettingsService.ResolveDataRoot(_settings.Settings), "secrets.local.key");

    private static string ResolveFallbackKeyDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localApplicationData))
                return Path.Combine(localApplicationData, "Hermaeus-Protected");
        }

        if (OperatingSystem.IsMacOS())
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
                return Path.Combine(userProfile, "Library", "Preferences", "Hermaeus", "protected");
        }

        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(applicationData))
            return Path.Combine(applicationData, "Hermaeus", "protected");

        var fallbackProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(fallbackProfile))
            return Path.Combine(fallbackProfile, ".config", "Hermaeus", "protected");

        throw new InvalidOperationException("A user-specific configuration directory is required for fallback secret key material.");
    }

    private byte[] GetOrCreateLocalKeyMaterial()
    {
        var path = KeyPath();
        if (File.Exists(path))
        {
            var existing = ReadLocalKeyMaterial(path);
            RemoveLegacyKeyAfterMigration(path);
            return existing;
        }

        var legacyPath = LegacyKeyPath();
        if (!string.Equals(path, legacyPath, LocalPathComparison) && File.Exists(legacyPath))
        {
            var legacy = ReadLocalKeyMaterial(legacyPath);
            ValidateLegacyVaultForRelocation(legacy);
            WriteLocalKeyMaterial(path, legacy);
            RemoveLegacyKeyAfterMigration(path);
            return legacy;
        }

        var key = RandomNumberGenerator.GetBytes(32);
        WriteLocalKeyMaterial(path, key);
        return key;
    }

    private void TryMigrateLegacyKey()
    {
        var path = KeyPath();
        var legacyPath = LegacyKeyPath();
        if (string.Equals(path, legacyPath, LocalPathComparison)
            || !File.Exists(legacyPath))
            return;

        byte[] legacy = [];
        byte[] current = [];
        try
        {
            legacy = ReadLocalKeyMaterial(legacyPath);
            ValidateLegacyVaultForRelocation(legacy);
            if (!File.Exists(path))
                WriteLocalKeyMaterial(path, legacy);
            else
            {
                current = ReadLocalKeyMaterial(path);
                if (!CryptographicOperations.FixedTimeEquals(current, legacy))
                    throw new CryptographicException("The fallback key relocation requires explicit investigation.");
            }
            RemoveLegacyKeyAfterMigration(path);
        }
        catch
        {
            _log?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Warning,
                RuntimeLogCategory.Service,
                "The legacy fallback secret key could not be migrated out of the data root. Fallback credentials may need to be re-entered."));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(legacy);
            CryptographicOperations.ZeroMemory(current);
        }
    }

    private void ValidateLegacyVaultForRelocation(byte[] candidateKey)
    {
        var vault = PathForStore();
        if (!File.Exists(vault)) return;
        var json = File.ReadAllBytes(vault);
        try
        {
            _ = new UTF8Encoding(false, true).GetCharCount(json);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new CryptographicException("The fallback vault cannot validate key relocation.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                FallbackVaultCodec.ValidateNormalizedName(property.Name);
                if (!names.Add(property.Name) || property.Value.ValueKind != JsonValueKind.String)
                    throw new CryptographicException("The fallback vault cannot validate key relocation.");
                // Explicit legacy dispatch refuses v3/unsupported versions before any copy or deletion.
                _ = DecodeLegacyValue(property.Value.GetString()!, candidateKey);
            }
        }
        finally { CryptographicOperations.ZeroMemory(json); }
    }

    private static byte[] ReadLocalKeyMaterial(string path)
    {
        var key = Convert.FromBase64String(File.ReadAllText(path).Trim());
        if (key.Length != 32)
            throw new CryptographicException("The local fallback secret key has an invalid length.");
        return key;
    }

    private static void WriteLocalKeyMaterial(string path, byte[] key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TryRestrictDirectoryPermissions(Path.GetDirectoryName(path)!);

        // Key material lives in a user-specific configuration directory, not
        // the portable data root that contains the encrypted vault. Restrict
        // the temp file before moving it into place, leaving no world-readable
        // window and no partially written primary key after a crash.
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, Convert.ToBase64String(key));
            TryRestrictPermissions(temp);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private void RemoveLegacyKeyAfterMigration(string protectedPath)
    {
        var legacyPath = LegacyKeyPath();
        if (string.Equals(protectedPath, legacyPath, LocalPathComparison)
            || !File.Exists(legacyPath))
            return;

        byte[] current = [];
        byte[] legacy = [];
        try
        {
            current = ReadLocalKeyMaterial(protectedPath);
            legacy = ReadLocalKeyMaterial(legacyPath);
            // GetOrCreateLocalKeyMaterial also reaches this cleanup path on ordinary v2 writes.
            if (CryptographicOperations.FixedTimeEquals(current, legacy)) TryDelete(legacyPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException) { }
        finally
        {
            CryptographicOperations.ZeroMemory(current);
            CryptographicOperations.ZeroMemory(legacy);
        }
        if (File.Exists(legacyPath))
        {
            _log?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Warning,
                RuntimeLogCategory.Service,
                "The legacy fallback secret key could not be removed from the data root. Remove secrets.local.key from the data root after closing Hermaeus."));
        }
    }

    private static void TryRestrictDirectoryPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        catch
        {
        }
    }

    private string EncryptSecret(string secret)
    {
        var plainBytes = Encoding.UTF8.GetBytes(secret);
        var encryptedBytes = EncryptWithAes(plainBytes);
        return EncryptedPrefix + Convert.ToBase64String(encryptedBytes);
    }

    private string DecryptSecret(string encrypted)
    {
        byte[] key = [];
        try
        {
            var format = FallbackVaultCodec.Classify(encrypted);
            if (format is FallbackVaultFormat.V3 or FallbackVaultFormat.UnsupportedVersion)
                throw new CryptographicException("The local vault format is unavailable.");
            // Resolving an existing credential must never manufacture a replacement key.
            if (format == FallbackVaultFormat.LegacyV2) key = ReadLocalKeyMaterial(KeyPath());
            return DecodeLegacyValue(encrypted, key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or FormatException or ArgumentException)
        {
            LogUnavailableSecret();
            return string.Empty;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private void LogUnavailableSecret() => _log?.Add(new RuntimeLogEntry(
        DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service,
        "A stored secret could not be decrypted (the local secret key file may be corrupt or the payload is invalid). The provider using it will behave as if no credential is configured."));

    private static string DecodeLegacyValue(string value, byte[] key)
    {
        var format = FallbackVaultCodec.Classify(value);
        byte[] plaintext;
        if (format == FallbackVaultFormat.LegacyPlaintext) plaintext = Convert.FromBase64String(value);
        else if (format == FallbackVaultFormat.LegacyV2 && key.Length == 32)
            plaintext = DecryptWithAes(Convert.FromBase64String(value[EncryptedPrefix.Length..]), key);
        else throw new CryptographicException("The local vault value is unavailable.");
        try { return new UTF8Encoding(false, true).GetString(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private byte[] EncryptWithAes(byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = DeriveKey(GetOrCreateLocalKeyMaterial(), salt);
        using var encryptor = aes.CreateEncryptor(key, aes.IV);
        using var ms = new MemoryStream();
        ms.Write(salt, 0, salt.Length);
        ms.Write(aes.IV, 0, aes.IV.Length);

        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            cs.Write(plaintext, 0, plaintext.Length);
            cs.FlushFinalBlock();
        }

        return ms.ToArray();
    }

    private static byte[] DecryptWithAes(byte[] ciphertext, byte[] keyMaterial)
    {
        if (ciphertext.Length >= SaltBytes + AesIvBytes + 1)
        {
            try
            {
                var salt = new byte[SaltBytes];
                Array.Copy(ciphertext, 0, salt, 0, salt.Length);
                var plain = DecryptPayload(ciphertext, SaltBytes, DeriveKey(keyMaterial, salt));
                try { _ = new UTF8Encoding(false, true).GetCharCount(plain); return plain; }
                catch (DecoderFallbackException) { CryptographicOperations.ZeroMemory(plain); throw; }
            }
            catch (CryptographicException)
            {
                // Values written before per-secret salts were introduced have
                // the same v2 prefix but contain only IV + ciphertext.
            }
            catch (ArgumentException)
            {
            }
        }

        return DecryptPayload(ciphertext, 0, DeriveLegacyKey(keyMaterial));
    }

    private static byte[] DecryptPayload(byte[] ciphertext, int offset, byte[] key)
    {
        try
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var iv = new byte[AesIvBytes];
            if (ciphertext.Length < offset + iv.Length)
                throw new CryptographicException("Invalid local secret payload.");

            Array.Copy(ciphertext, offset, iv, 0, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(key, aes.IV);
            using var ms = new MemoryStream(ciphertext, offset + iv.Length, ciphertext.Length - offset - iv.Length);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var resultMs = new MemoryStream();
            cs.CopyTo(resultMs);
            return resultMs.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] DeriveKey(byte[] keyMaterial, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(keyMaterial, salt, KeyIterations, HashAlgorithmName.SHA256, 32);
    }

    private static byte[] DeriveLegacyKey(byte[] keyMaterial) =>
        DeriveKey(keyMaterial, Encoding.UTF8.GetBytes("hermaeus-secret-store"));

    private interface ISecretBackend
    {
        string Label { get; }
        Task<bool> IsAvailableAsync(CancellationToken ct);
        Task<bool> StoreAsync(string name, string secret, CancellationToken ct);
        Task<string> ResolveAsync(string name, CancellationToken ct);
    }

    private sealed class NullSecretBackend : ISecretBackend
    {
        public static readonly NullSecretBackend Instance = new();
        public string Label => "Unavailable";
        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<bool> StoreAsync(string name, string secret, CancellationToken ct) => Task.FromResult(false);
        public Task<string> ResolveAsync(string name, CancellationToken ct) => Task.FromResult(string.Empty);
    }

    private sealed class LinuxSecretServiceBackend : CommandSecretBackend
    {
        public override string Label => "Linux Secret Service";
        public override async Task<bool> IsAvailableAsync(CancellationToken ct) =>
            (await RunAsync("secret-tool", ["--version"], null, ct)).Started;

        public override async Task<bool> StoreAsync(string name, string secret, CancellationToken ct)
        {
            var result = await RunAsync("secret-tool",
                ["store", "--label", $"Hermaeus {name}", "application", "Hermaeus", "name", name],
                secret,
                ct);
            return result.Success;
        }

        public override async Task<string> ResolveAsync(string name, CancellationToken ct)
        {
            var result = await RunAsync("secret-tool", ["lookup", "application", "Hermaeus", "name", name], null, ct);
            return result.Success ? result.Output.TrimEnd('\r', '\n') : string.Empty;
        }
    }

    /// <summary>
    /// Latent: Hermaeus's shipped scope is Windows and Linux only (AGENTS.md).
    /// Unlike <see cref="LinuxSecretServiceBackend"/>, the macOS <c>security</c>
    /// CLI has no stdin form for <c>add-generic-password -w</c>, so the secret
    /// is necessarily passed as a process argument here and is visible to any
    /// other local process listing arguments (e.g. `ps`) for the call's
    /// duration. If macOS ever becomes a supported target, prefer the
    /// encrypted local-file fallback over this backend, or shell out to a
    /// short-lived helper that reads the secret from stdin instead
    /// (docs/review/01-code-audit.md P3-6).
    /// </summary>
    private sealed class MacOsKeychainBackend : CommandSecretBackend
    {
        public override string Label => "macOS Keychain";
        public override async Task<bool> IsAvailableAsync(CancellationToken ct) =>
            (await RunAsync("security", ["help"], null, ct)).Started;

        public override async Task<bool> StoreAsync(string name, string secret, CancellationToken ct)
        {
            var result = await RunAsync("security",
                ["add-generic-password", "-a", name, "-s", "Hermaeus", "-w", secret, "-U"],
                null,
                ct);
            return result.Success;
        }

        public override async Task<string> ResolveAsync(string name, CancellationToken ct)
        {
            var result = await RunAsync("security", ["find-generic-password", "-a", name, "-s", "Hermaeus", "-w"], null, ct);
            return result.Success ? result.Output.TrimEnd('\r', '\n') : string.Empty;
        }
    }

    internal static Task<CommandResult> RunOsSecretCommandAsync(
        string file, string[] args, string? stdin, CancellationToken ct, TimeSpan? timeoutBudget = null) =>
        CommandSecretBackend.RunAsync(file, args, stdin, ct, timeoutBudget);

    private abstract class CommandSecretBackend : ISecretBackend
    {
        public abstract string Label { get; }
        public abstract Task<bool> IsAvailableAsync(CancellationToken ct);
        public abstract Task<bool> StoreAsync(string name, string secret, CancellationToken ct);
        public abstract Task<string> ResolveAsync(string name, CancellationToken ct);

        internal static async Task<CommandResult> RunAsync(string file, string[] args, string? stdin, CancellationToken ct,
            TimeSpan? timeoutBudget = null)
        {
            if (ct.IsCancellationRequested) return new CommandResult(false, false, string.Empty);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = file,
                    RedirectStandardInput = stdin is not null,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (var arg in args)
                process.StartInfo.ArgumentList.Add(arg);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutBudget ?? TimeSpan.FromSeconds(8));
            Task<(string Text, bool Truncated)>? stdout = null;
            Task? stderr = null;
            var started = false;
            try
            {
                if (!process.Start()) return new CommandResult(false, false, string.Empty);
                started = true;
                // Start both drains before writing. A keychain command may
                // fill stderr before it consumes the supplied secret.
                stdout = ReadSecretOutputAsync(process.StandardOutput, timeout);
                stderr = DrainSecretErrorAsync(process.StandardError, timeout);
                if (stdin is not null)
                {
                    await process.StandardInput.WriteAsync(stdin.AsMemory(), timeout.Token);
                    await process.StandardInput.FlushAsync(timeout.Token);
                    process.StandardInput.Close();
                }

                await Task.WhenAll(stdout, stderr);
                await process.WaitForExitAsync(timeout.Token);
                return stdout.Result.Truncated
                    ? new CommandResult(true, false, string.Empty)
                    : new CommandResult(true, process.ExitCode == 0, stdout.Result.Text);
            }
            catch
            {
                if (started)
                    await StopSecretCommandAsync(process, timeout, stdout, stderr);
                return new CommandResult(false, false, string.Empty);
            }
        }

        private static async Task<(string Text, bool Truncated)> ReadSecretOutputAsync(
            StreamReader reader, CancellationTokenSource cancellation)
        {
            const int limit = 64 * 1024;
            var output = new StringBuilder();
            var buffer = new char[4096];
            var truncated = false;
            try
            {
                int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation.Token)) > 0)
                {
                    var keep = Math.Min(count, limit - output.Length);
                    output.Append(buffer, 0, keep);
                    truncated |= keep < count;
                }
                return (output.ToString(), truncated);
            }
            catch
            {
                try { cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
                throw;
            }
        }

        private static async Task DrainSecretErrorAsync(StreamReader reader, CancellationTokenSource cancellation)
        {
            var buffer = new char[4096];
            try
            {
                // Error output can contain credentials. Drain it without
                // retaining it or forwarding it to logs or diagnostics.
                while (await reader.ReadAsync(buffer.AsMemory(), cancellation.Token) > 0) { }
            }
            catch
            {
                try { cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
                throw;
            }
        }

        private static async Task StopSecretCommandAsync(
            Process process, CancellationTokenSource readers, Task? stdout, Task? stderr)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
            {
                Console.Error.WriteLine($"Secret command tree termination could not be confirmed ({ex.GetType().Name}).");
            }
            readers.Cancel();
            var joined = Task.WhenAll(new[] { stdout, stderr }.OfType<Task>().Append(process.WaitForExitAsync()));
            try { await joined.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (OperationCanceledException) when (joined.IsCanceled) { }
            catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException or DecoderFallbackException
                or InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
            {
                // Only fixed reason/type information is emitted. Never
                // include exception messages, arguments or captured text.
                Console.Error.WriteLine($"Secret command root or output cleanup could not be confirmed ({ex.GetType().Name}).");
                _ = joined.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    internal sealed record CommandResult(bool Started, bool Success, string Output);

    private sealed class WindowsCredentialBackend : ISecretBackend
    {
        private const int CredTypeGeneric = 1;
        private const int CredPersistLocalMachine = 2;
        public string Label => "Windows Credential Manager";
        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(OperatingSystem.IsWindows());

        public Task<bool> StoreAsync(string name, string secret, CancellationToken ct)
        {
            if (!OperatingSystem.IsWindows()) return Task.FromResult(false);
            var bytes = Encoding.Unicode.GetBytes(secret);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target(name),
                CredentialBlobSize = (uint)bytes.Length,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName
            };

            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                credential.CredentialBlob = handle.AddrOfPinnedObject();
                return Task.FromResult(CredWrite(ref credential, 0));
            }
            finally
            {
                handle.Free();
            }
        }

        public Task<string> ResolveAsync(string name, CancellationToken ct)
        {
            if (!OperatingSystem.IsWindows()) return Task.FromResult(string.Empty);
            if (!CredRead(Target(name), CredTypeGeneric, 0, out var credentialPtr) || credentialPtr == IntPtr.Zero)
                return Task.FromResult(string.Empty);

            try
            {
                var credential = Marshal.PtrToStructure<Credential>(credentialPtr);
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                    return Task.FromResult(string.Empty);

                var bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Task.FromResult(Encoding.Unicode.GetString(bytes));
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }

        private static string Target(string name) => $"Hermaeus/{name}";

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref Credential userCredential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
        private static extern void CredFree(IntPtr buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string? Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }
    }
}
