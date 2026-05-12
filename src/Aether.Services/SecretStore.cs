using System.Diagnostics;
using System.Runtime.InteropServices;
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
        all[name] = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
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
        if (!all.TryGetValue(key, out var encoded)) return string.Empty;
        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
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
