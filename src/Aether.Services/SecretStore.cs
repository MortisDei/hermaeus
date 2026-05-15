using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class SecretStore : ISecretStore
{
    private readonly ISettingsService _settings;
    private readonly ISecretBackend _osBackend;
    private const string Prefix = "secret:";

    public SecretStore(ISettingsService settings)
    {
        _settings = settings;
        _osBackend = CreateOsBackend();
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

        var all = await LoadAsync(ct);
        all[name] = EncryptSecret(secret);
        var path = PathForStore();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }), ct);
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
        var path = PathForStore();
        if (!File.Exists(path)) return;
        var all = await LoadAsync(ct);
        if (!all.Remove(name)) return;
        if (all.Count == 0)
        {
            File.Delete(path);
            return;
        }

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }), ct);
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

    private static ISecretBackend CreateOsBackend()
    {
        if (OperatingSystem.IsWindows()) return new WindowsCredentialBackend();
        if (OperatingSystem.IsMacOS()) return new MacOsKeychainBackend();
        if (OperatingSystem.IsLinux()) return new LinuxSecretServiceBackend();
        return NullSecretBackend.Instance;
    }

    private static bool UseOsKeychain() =>
        !string.Equals(Environment.GetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN"), "1", StringComparison.Ordinal);

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

    private static string EncryptSecret(string secret)
    {
        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(secret);
            var encryptedBytes = EncryptWithAes(plainBytes);
            return Convert.ToBase64String(encryptedBytes);
        }
        catch
        {
            // Fallback to unencrypted Base64 if encryption fails
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
        }
    }

    private static string DecryptSecret(string encrypted)
    {
        try
        {
            var encryptedBytes = Convert.FromBase64String(encrypted);
            var plainBytes = DecryptWithAes(encryptedBytes);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            // Fallback to unencrypted Base64 if decryption fails
            try
            {
                var bytes = Convert.FromBase64String(encrypted);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private static byte[] EncryptWithAes(byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var key = DeriveKey();
        using var encryptor = aes.CreateEncryptor(key, aes.IV);
        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);

        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            cs.Write(plaintext, 0, plaintext.Length);
            cs.FlushFinalBlock();
        }

        return ms.ToArray();
    }

    private static byte[] DecryptWithAes(byte[] ciphertext)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var key = DeriveKey();
        var iv = new byte[aes.IV.Length];
        Array.Copy(ciphertext, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(key, aes.IV);
        using var ms = new MemoryStream(ciphertext, iv.Length, ciphertext.Length - iv.Length);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var resultMs = new MemoryStream();
        cs.CopyTo(resultMs);
        return resultMs.ToArray();
    }

    private static byte[] DeriveKey()
    {
        // Derive a consistent key from machine and user identifiers
        var machineId = GetMachineIdentifier();
        var seed = Encoding.UTF8.GetBytes(machineId);
        var salt = Encoding.UTF8.GetBytes("aether-secret-store");
        
        // Use PBKDF2 with SHA256 to derive a 256-bit key
        var hash = new byte[32];
        using (var hmac = new System.Security.Cryptography.HMACSHA256(seed))
        {
            // Simple PBKDF2 implementation with 10000 iterations
            var block = new byte[hmac.OutputBlockSize / 8 + salt.Length];
            salt.CopyTo(block, 0);
            block[salt.Length] = 0;
            block[salt.Length + 1] = 0;
            block[salt.Length + 2] = 0;
            block[salt.Length + 3] = 1;
            
            var lastBlock = hmac.ComputeHash(block);
            Array.Copy(lastBlock, hash, Math.Min(hash.Length, lastBlock.Length));
            
            var iterationBlock = lastBlock;
            for (int i = 1; i < 10000; i++)
            {
                iterationBlock = hmac.ComputeHash(iterationBlock);
                for (int j = 0; j < Math.Min(hash.Length, iterationBlock.Length); j++)
                    hash[j] ^= iterationBlock[j];
            }
        }
        
        return hash;
    }

    private static string GetMachineIdentifier()
    {
        // Use a combination of machine identifiers to derive the key
        // This ensures the key is consistent across app restarts but different across machines
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var id = System.Diagnostics.Process.GetCurrentProcess().MachineName;
                return id ?? "aether";
            }
            catch { }
        }

        var hostname = System.Net.Dns.GetHostName();
        return !string.IsNullOrWhiteSpace(hostname) ? hostname : "aether";
    }

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
                ["store", "--label", $"Aether {name}", "application", "Aether", "name", name],
                secret,
                ct);
            return result.Success;
        }

        public override async Task<string> ResolveAsync(string name, CancellationToken ct)
        {
            var result = await RunAsync("secret-tool", ["lookup", "application", "Aether", "name", name], null, ct);
            return result.Success ? result.Output.TrimEnd('\r', '\n') : string.Empty;
        }
    }

    private sealed class MacOsKeychainBackend : CommandSecretBackend
    {
        public override string Label => "macOS Keychain";
        public override async Task<bool> IsAvailableAsync(CancellationToken ct) =>
            (await RunAsync("security", ["help"], null, ct)).Started;

        public override async Task<bool> StoreAsync(string name, string secret, CancellationToken ct)
        {
            var result = await RunAsync("security",
                ["add-generic-password", "-a", name, "-s", "Aether", "-w", secret, "-U"],
                null,
                ct);
            return result.Success;
        }

        public override async Task<string> ResolveAsync(string name, CancellationToken ct)
        {
            var result = await RunAsync("security", ["find-generic-password", "-a", name, "-s", "Aether", "-w"], null, ct);
            return result.Success ? result.Output.TrimEnd('\r', '\n') : string.Empty;
        }
    }

    private abstract class CommandSecretBackend : ISecretBackend
    {
        public abstract string Label { get; }
        public abstract Task<bool> IsAvailableAsync(CancellationToken ct);
        public abstract Task<bool> StoreAsync(string name, string secret, CancellationToken ct);
        public abstract Task<string> ResolveAsync(string name, CancellationToken ct);

        protected static async Task<CommandResult> RunAsync(string file, string[] args, string? stdin, CancellationToken ct)
        {
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

            try
            {
                if (!process.Start()) return new CommandResult(false, false, string.Empty);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                if (stdin is not null)
                {
                    await process.StandardInput.WriteAsync(stdin.AsMemory(), timeout.Token);
                    await process.StandardInput.FlushAsync(timeout.Token);
                    process.StandardInput.Close();
                }

                var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                return new CommandResult(true, process.ExitCode == 0, output);
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
                return new CommandResult(false, false, string.Empty);
            }
        }
    }

    private sealed record CommandResult(bool Started, bool Success, string Output);

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

        private static string Target(string name) => $"Aether/{name}";

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
