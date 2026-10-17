using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class SecretStoreAuthenticatedTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string? _previous = Environment.GetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN");
    private readonly SettingsService _settings;
    private readonly string _protected;
    private string Vault => Path.Combine(_settings.Settings.DataManagement.DataRootDirectory, "secrets.local.json");
    private string KeyPath => Path.Combine(_protected, "secrets.local.key");

    public SecretStoreAuthenticatedTests()
    {
        Environment.SetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN", "1");
        _settings = Helpers.NewSettings(_temp);
        _protected = _temp.PathFor("protected");
    }

    [Fact]
    public async Task Default_constructor_chain_keeps_v2_and_explicit_test_optin_migrates_whole_vault()
    {
        var legacy = new SecretStore(_settings, null, _protected);
        var reference = await legacy.StoreAsync("  Name  ", "synthetic-old");
        Assert.Equal("secret:Name", reference);
        var original = await File.ReadAllBytesAsync(Vault);
        Assert.StartsWith("v2:", ReadValues()["Name"]);
        Assert.Equal("synthetic-old", await legacy.ResolveAsync(reference));
        var authenticated = new SecretStore(_settings, null, _protected, true);
        await authenticated.StoreAsync("second", "synthetic-new");
        Assert.All(ReadValues().Values, value => Assert.StartsWith("v3:", value));
        Assert.Equal("synthetic-old", await authenticated.ResolveAsync(reference));
        Assert.Equal("synthetic-new", await new SecretStore(_settings, null, _protected, true).ResolveAsync("secret:second"));
        Assert.Single(Directory.GetFiles(_protected, "*.pre-v3.snapshot"));
        Assert.Single(Directory.GetFiles(_protected, "*.pre-v3.locator"));
        var snapshotPath = Directory.GetFiles(_protected, "*.pre-v3.snapshot").Single();
        var id = Path.GetFileName(snapshotPath)[..64];
        Assert.Equal(original, FallbackVaultCodec.DecryptSnapshot(await File.ReadAllTextAsync(snapshotPath),
            Convert.FromBase64String(await File.ReadAllTextAsync(KeyPath)), Convert.FromHexString(id)));
        Assert.NotEqual(original, await File.ReadAllBytesAsync(Vault));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_optin_and_missing_or_corrupt_key_never_replace_original(bool corrupt)
    {
        var store = new SecretStore(_settings, null, _protected, true);
        await store.StoreAsync("name", "synthetic-original");
        var originalVault = await File.ReadAllBytesAsync(Vault);
        if (corrupt) await File.WriteAllTextAsync(KeyPath, "not-a-valid-key");
        else File.Delete(KeyPath);
        var damagedKey = corrupt ? await File.ReadAllBytesAsync(KeyPath) : null;
        Assert.Equal(string.Empty, await store.ResolveAsync("secret:name"));
        await Assert.ThrowsAsync<IOException>(() => store.StoreAsync("new", "synthetic-new"));
        Assert.Equal(originalVault, await File.ReadAllBytesAsync(Vault));
        if (corrupt) Assert.Equal(damagedKey, await File.ReadAllBytesAsync(KeyPath));
        else Assert.False(File.Exists(KeyPath));
    }

    [Theory]
    [InlineData("plaintext")]
    [InlineData("salted")]
    [InlineData("unsalted")]
    public async Task Every_legacy_form_resolves_then_migrates_with_original_key(string format)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        WriteKey(key);
        var value = format == "plaintext" ? Convert.ToBase64String("synthetic-秘密"u8)
            : LegacyCiphertext(key, "synthetic-秘密", format == "salted");
        WriteVault(new() { ["Name"] = value });
        var store = new SecretStore(_settings, null, _protected, true);
        Assert.Equal("synthetic-秘密", await store.ResolveAsync("secret:Name"));
        await store.StoreAsync("second", "synthetic-second");
        Assert.Equal("synthetic-秘密", await store.ResolveAsync("secret:Name"));
        Assert.Equal(key, Convert.FromBase64String(await File.ReadAllTextAsync(KeyPath)));
        Assert.All(ReadValues().Values, item => Assert.StartsWith("v3:", item));
    }

    [Theory]
    [InlineData("v3:c3ludGhldGlj")]
    [InlineData("V3:c3ludGhldGlj")]
    [InlineData("v4:c3ludGhldGlj")]
    public async Task Inactive_reader_never_uses_legacy_fallback_or_overwrites_recognised_versions(string value)
    {
        WriteVault(new() { ["name"] = value });
        var log = new RuntimeLogService(_settings);
        var store = new SecretStore(_settings, log, _protected);
        Assert.Equal(string.Empty, await store.ResolveAsync("secret:name"));
        var original = await File.ReadAllBytesAsync(Vault);
        await Assert.ThrowsAsync<CryptographicException>(() => store.StoreAsync("name", "synthetic-new"));
        Assert.Equal(original, await File.ReadAllBytesAsync(Vault));
        Assert.False(File.Exists(KeyPath));
        Assert.Single(log.GetEntries());
        Assert.DoesNotContain(log.GetEntries(), entry => entry.Message.Contains("synthetic", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inactive_v2_read_with_missing_key_does_not_create_replacement()
    {
        WriteVault(new() { ["name"] = LegacyCiphertext(RandomNumberGenerator.GetBytes(32), "synthetic", true) });
        var store = new SecretStore(_settings, null, _protected);
        Assert.Equal(string.Empty, await store.ResolveAsync("secret:name"));
        Assert.False(File.Exists(KeyPath));
        var original = await File.ReadAllBytesAsync(Vault);
        await Assert.ThrowsAnyAsync<IOException>(() => store.StoreAsync("second", "synthetic-new"));
        Assert.False(File.Exists(KeyPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(Vault));
    }

    [Fact]
    public async Task Authenticated_corruption_preserves_original_and_logs_only_fixed_diagnostic()
    {
        var log = new RuntimeLogService(_settings);
        var store = new SecretStore(_settings, log, _protected, true);
        await store.StoreAsync("sensitive-name", "synthetic-sensitive-secret");
        var values = ReadValues();
        var bytes = Convert.FromBase64String(values["sensitive-name"][3..]);
        bytes[28] ^= 1;
        values["sensitive-name"] = "v3:" + Convert.ToBase64String(bytes);
        WriteVault(values);
        var damagedVault = await File.ReadAllBytesAsync(Vault);
        Assert.Equal(string.Empty, await store.ResolveAsync("secret:sensitive-name"));
        await Assert.ThrowsAsync<IOException>(() => store.StoreAsync("second", "synthetic-new"));
        Assert.Equal(damagedVault, await File.ReadAllBytesAsync(Vault));
        Assert.Single(log.GetEntries());
        Assert.DoesNotContain(log.GetEntries(), entry => entry.Message.Contains("sensitive", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_Data_Root_transition_blocks_optin_mutation_and_read(bool plan)
    {
        var store = new SecretStore(_settings, null, _protected, true);
        await store.StoreAsync("name", "synthetic");
        var original = await File.ReadAllBytesAsync(Vault);
        if (plan) _settings.Settings.DataManagement.PendingDataRootMigrationPlan = "synthetic-pending-plan";
        else _settings.Settings.DataManagement.PendingDataRootDirectory = _temp.PathFor("next");
        Assert.Equal(string.Empty, await store.ResolveAsync("secret:name"));
        await Assert.ThrowsAsync<IOException>(() => store.StoreAsync("name", "synthetic-new"));
        Assert.Equal(original, await File.ReadAllBytesAsync(Vault));
    }

    [Fact]
    public async Task Data_Root_move_preserves_v3_resolution_using_same_protected_key()
    {
        var store = new SecretStore(_settings, null, _protected, true);
        await store.StoreAsync("name", "synthetic");
        var key = await File.ReadAllBytesAsync(KeyPath);
        var oldVault = Vault;
        _settings.Settings.DataManagement.DataRootDirectory = _temp.PathFor("moved");
        Directory.CreateDirectory(Path.GetDirectoryName(Vault)!);
        File.Move(oldVault, Vault);
        Assert.Equal("synthetic", await store.ResolveAsync("secret:name"));
        await store.StoreAsync("second", "synthetic-second");
        Assert.Equal(key, await File.ReadAllBytesAsync(KeyPath));
    }

    [Fact]
    public async Task Legacy_key_relocation_occurs_only_inside_optin_operation()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var legacyPath = Path.Combine(_settings.Settings.DataManagement.DataRootDirectory, "secrets.local.key");
        WriteVault(new() { ["name"] = LegacyCiphertext(key, "synthetic", true) });
        File.WriteAllText(legacyPath, Convert.ToBase64String(key));
        Restrict(legacyPath);
        var store = new SecretStore(_settings, null, _protected, true);
        Assert.True(File.Exists(legacyPath));
        Assert.False(File.Exists(KeyPath));
        Assert.Equal("synthetic", await store.ResolveAsync("secret:name"));
        Assert.False(File.Exists(legacyPath));
        Assert.Equal(key, Convert.FromBase64String(await File.ReadAllTextAsync(KeyPath)));
    }

    [Theory]
    [InlineData("v3")]
    [InlineData("unknown")]
    [InlineData("malformed")]
    [InlineData("wrong-v2-key")]
    [InlineData("duplicate")]
    public async Task Disabled_constructor_does_not_publish_unproven_legacy_key_or_delete_source(string scenario)
    {
        var candidate = RandomNumberGenerator.GetBytes(32);
        var actual = RandomNumberGenerator.GetBytes(32);
        WriteVault(new() { ["name"] = scenario switch
        {
            "v3" => FallbackVaultCodec.EncryptValue("name", "synthetic", actual),
            "unknown" => "v4:c3ludGhldGlj",
            _ => LegacyCiphertext(actual, "synthetic", true)
        } });
        if (scenario == "malformed") File.WriteAllText(Vault, "{broken");
        if (scenario == "duplicate") File.WriteAllText(Vault, "{\"name\":\"c3ludGhldGlj\",\"name\":\"c3ludGhldGlj\"}");
        var legacyPath = Path.Combine(Path.GetDirectoryName(Vault)!, "secrets.local.key");
        File.WriteAllText(legacyPath, Convert.ToBase64String(candidate));
        Restrict(legacyPath);
        var original = await File.ReadAllBytesAsync(Vault);
        _ = new SecretStore(_settings, null, _protected);
        Assert.False(File.Exists(KeyPath));
        Assert.Equal(candidate, Convert.FromBase64String(await File.ReadAllTextAsync(legacyPath)));
        Assert.Equal(original, await File.ReadAllBytesAsync(Vault));
    }

    [Theory]
    [InlineData("plaintext")]
    [InlineData("salted")]
    [InlineData("unsalted")]
    public async Task Disabled_constructor_preserves_valid_legacy_key_relocation(string format)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        WriteVault(new() { ["name"] = format == "plaintext" ? Convert.ToBase64String("synthetic"u8)
            : LegacyCiphertext(key, "synthetic", format == "salted") });
        var legacyPath = Path.Combine(Path.GetDirectoryName(Vault)!, "secrets.local.key");
        File.WriteAllText(legacyPath, Convert.ToBase64String(key));
        Restrict(legacyPath);
        var original = await File.ReadAllBytesAsync(Vault);
        var store = new SecretStore(_settings, null, _protected);
        Assert.False(File.Exists(legacyPath));
        Assert.Equal(key, Convert.FromBase64String(await File.ReadAllTextAsync(KeyPath)));
        Assert.Equal(original, await File.ReadAllBytesAsync(Vault));
        Assert.Equal("synthetic", await store.ResolveAsync("secret:name"));
    }

    [Fact]
    public async Task Disabled_constructor_preserves_both_mismatched_keys()
    {
        var original = RandomNumberGenerator.GetBytes(32);
        var candidate = RandomNumberGenerator.GetBytes(32);
        WriteKey(original);
        WriteVault(new() { ["name"] = Convert.ToBase64String("synthetic"u8) });
        var legacyPath = Path.Combine(Path.GetDirectoryName(Vault)!, "secrets.local.key");
        File.WriteAllText(legacyPath, Convert.ToBase64String(candidate));
        Restrict(legacyPath);
        var store = new SecretStore(_settings, null, _protected);
        await store.StoreAsync("second", "synthetic-second");
        Assert.Equal(original, Convert.FromBase64String(await File.ReadAllTextAsync(KeyPath)));
        Assert.Equal(candidate, Convert.FromBase64String(await File.ReadAllTextAsync(legacyPath)));
    }

    [Fact]
    public async Task Disabled_legacy_key_relocation_uses_platform_path_case_semantics()
    {
        _settings.Settings.DataManagement.DataRootDirectory = _temp.PathFor("caseroot");
        var protectedDirectory = _temp.PathFor("CaseRoot");
        var key = RandomNumberGenerator.GetBytes(32);
        WriteVault(new() { ["name"] = LegacyCiphertext(key, "synthetic-original", true) });
        var legacyPath = Path.Combine(_settings.Settings.DataManagement.DataRootDirectory, "secrets.local.key");
        var protectedPath = Path.Combine(protectedDirectory, "secrets.local.key");
        await File.WriteAllTextAsync(legacyPath, Convert.ToBase64String(key));
        Restrict(legacyPath);

        var store = new SecretStore(_settings, null, protectedDirectory);
        Assert.Equal("synthetic-original", await store.ResolveAsync("secret:name"));
        Assert.Equal(key, Convert.FromBase64String(await File.ReadAllTextAsync(protectedPath)));
        Assert.Equal(OperatingSystem.IsWindows(), File.Exists(legacyPath));
        await store.StoreAsync("second", "synthetic-second");
        Assert.Equal("synthetic-original", await store.ResolveAsync("secret:name"));
        Assert.Equal("synthetic-second", await store.ResolveAsync("secret:second"));
        Assert.Equal(key, Convert.FromBase64String(await File.ReadAllTextAsync(protectedPath)));
        Assert.Equal(OperatingSystem.IsWindows(), File.Exists(legacyPath));
    }

    private Dictionary<string, string> ReadValues() => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Vault))!;
    private void WriteVault(Dictionary<string, string> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Vault)!);
        File.WriteAllText(Vault, JsonSerializer.Serialize(values));
        Restrict(Vault);
    }
    private void WriteKey(byte[] key)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(_protected);
        else Directory.CreateDirectory(_protected, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(KeyPath, Convert.ToBase64String(key));
        Restrict(KeyPath);
    }
    private static void Restrict(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    private static string LegacyCiphertext(byte[] key, string secret, bool salted)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var derived = Rfc2898DeriveBytes.Pbkdf2(key, salted ? salt : Encoding.UTF8.GetBytes("hermaeus-secret-store"),
            100_000, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = Aes.Create();
            aes.Key = derived;
            var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(secret), aes.IV);
            return "v2:" + Convert.ToBase64String((salted ? salt : []).Concat(aes.IV).Concat(cipher).ToArray());
        }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN", _previous);
        _temp.Dispose();
    }
}
