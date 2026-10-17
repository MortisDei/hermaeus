using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class FallbackVaultTransactionTests
{
    [Fact]
    public async Task Removing_from_fresh_absent_vault_does_not_create_key_or_primary()
    {
        using var fixture = new Fixture();
        await fixture.Transaction().MutateAsync("absent", null);
        Assert.False(File.Exists(fixture.Vault));
        Assert.False(File.Exists(Path.Combine(fixture.Protected, "secrets.local.key")));
    }

    [Fact]
    public async Task Fresh_writes_reopen_and_concurrent_instances_preserve_all_names()
    {
        using var fixture = new Fixture();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => fixture.Transaction().MutateAsync("name" + i, "synthetic" + i)));
        for (var i = 0; i < 8; i++) Assert.Equal("synthetic" + i, await fixture.Transaction().ResolveAsync("name" + i));
        Assert.Single(Directory.GetFiles(fixture.Protected, "secrets.local.key"));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Legacy_migration_keeps_encrypted_exact_snapshot_and_explicit_restore_loses_later_changes()
    {
        using var fixture = new Fixture();
        var original = fixture.Legacy("old", "synthetic-before");
        await fixture.Transaction().MutateAsync("old", "synthetic-after");
        await fixture.Transaction().MutateAsync("later", "synthetic-added");
        var candidate = Assert.Single(await fixture.Transaction().DiscoverRecoveryAsync());
        Assert.True(candidate.Available);
        var snapshot = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(fixture.Protected, "*.snapshot")));
        Assert.DoesNotContain("synthetic-before", snapshot);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("synthetic-before")), snapshot);
        var review = await fixture.Transaction().ReviewRecoveryAsync(candidate.OriginalId);
        await fixture.Transaction().RestoreAsync(review);
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Vault));
        Assert.Equal("synthetic-before", await fixture.Transaction().ResolveAsync("old"));
        Assert.Null(await fixture.Transaction().ResolveAsync("later"));
    }

    [Fact]
    public async Task Recovery_discovery_survives_root_move_and_refuses_changed_target_after_review()
    {
        using var fixture = new Fixture();
        fixture.Legacy("old", "synthetic-before");
        await fixture.Transaction().MutateAsync("old", "synthetic-after");
        var moved = Path.Combine(fixture.Root, "moved", "secrets.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(fixture.Vault, moved);
        var transaction = fixture.Transaction(path: moved);
        var selected = Assert.Single(await transaction.DiscoverRecoveryAsync());
        var review = await transaction.ReviewRecoveryAsync(selected.OriginalId);
        await transaction.MutateAsync("new", "synthetic-newer");
        var newer = await File.ReadAllBytesAsync(moved);
        await Assert.ThrowsAsync<IOException>(() => transaction.RestoreAsync(review));
        Assert.Equal(newer, await File.ReadAllBytesAsync(moved));
        var currentReview = await transaction.ReviewRecoveryAsync(selected.OriginalId);
        await transaction.RestoreAsync(currentReview);
        Assert.Equal("synthetic-before", await transaction.ResolveAsync("old"));
        Assert.Null(await transaction.ResolveAsync("new"));
    }

    [Theory]
    [InlineData("snapshot-flushed", false)]
    [InlineData("snapshot-replaced", false)]
    [InlineData("locator-flushed", false)]
    [InlineData("locator-replaced", false)]
    [InlineData("primary-flushed", false)]
    [InlineData("primary-replaced", true)]
    [InlineData("primary-reopened", true)]
    public async Task Injected_crash_boundaries_preserve_authoritative_primary(string phase, bool committed)
    {
        using var fixture = new Fixture();
        var original = fixture.Legacy("old", "synthetic-before");
        var transaction = fixture.Transaction(fault: at => { if (at == phase) throw new IOException("synthetic fault"); });
        await Assert.ThrowsAsync<IOException>(() => transaction.MutateAsync("old", "synthetic-after"));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp", SearchOption.AllDirectories));
        if (committed)
            Assert.Equal("synthetic-after", await fixture.Transaction().ResolveAsync("old"));
        else
        {
            Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Vault));
            // An authenticated snapshot-only interruption can complete its locator on retry.
            await fixture.Transaction().MutateAsync("old", "synthetic-after");
            Assert.Equal("synthetic-after", await fixture.Transaction().ResolveAsync("old"));
        }
    }

    [Theory]
    [InlineData("{\"x\":\"eA==\",\"x\":\"eQ==\"}")]
    [InlineData("{\" x \":\"eA==\"}")]
    [InlineData("{\"x\":null}")]
    [InlineData("{\"x\":\"v99:unsupported\"}")]
    [InlineData("{\"x\":\"v3:malformed\"}")]
    [InlineData("{\"x\":\"not base64\"}")]
    [InlineData("{\"\\ud800\":\"eA==\"}")]
    [InlineData("{\"x\":\"\\ud800\"}")]
    public async Task Invalid_existing_dictionary_is_never_silently_replaced(string json)
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Vault, json);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fixture.Vault, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var original = await File.ReadAllBytesAsync(fixture.Vault);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction().MutateAsync("other", "synthetic"));
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Vault));
        Assert.False(File.Exists(Path.Combine(fixture.Protected, "secrets.local.key")));
    }

    [Fact]
    public async Task Corrupt_locator_snapshot_and_missing_key_do_not_trigger_fallback_or_replacement()
    {
        using var fixture = new Fixture();
        fixture.Legacy("old", "synthetic-before");
        await fixture.Transaction().MutateAsync("old", "synthetic-after");
        var primary = await File.ReadAllBytesAsync(fixture.Vault);
        var locator = Assert.Single(Directory.GetFiles(fixture.Protected, "*.locator"));
        var contents = await File.ReadAllBytesAsync(locator);
        contents[^2] ^= 1;
        await File.WriteAllBytesAsync(locator, contents);
        Assert.False(Assert.Single(await fixture.Transaction().DiscoverRecoveryAsync()).Available);
        File.Delete(Path.Combine(fixture.Protected, "secrets.local.key"));
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction().ResolveAsync("old"));
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction().MutateAsync("other", "synthetic"));
        Assert.False(File.Exists(Path.Combine(fixture.Protected, "secrets.local.key")));
        Assert.Equal(primary, await File.ReadAllBytesAsync(fixture.Vault));
    }

    [Fact]
    public async Task Existing_nonmatching_snapshot_blocks_remigration_and_preserves_both_files()
    {
        using var fixture = new Fixture();
        fixture.Legacy("old", "synthetic-before");
        await fixture.Transaction().MutateAsync("old", "synthetic-after");
        var snapshot = Assert.Single(Directory.GetFiles(fixture.Protected, "*.snapshot"));
        var recovery = await File.ReadAllBytesAsync(snapshot);
        fixture.Legacy("old", "synthetic-different");
        var primary = await File.ReadAllBytesAsync(fixture.Vault);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction().MutateAsync("new", "synthetic"));
        Assert.Equal(primary, await File.ReadAllBytesAsync(fixture.Vault));
        Assert.Equal(recovery, await File.ReadAllBytesAsync(snapshot));
    }

    [Fact]
    public async Task Missing_primary_with_existing_key_refuses_implicit_new_vault_and_empty_vault_is_retained()
    {
        using var fixture = new Fixture();
        await fixture.Transaction().MutateAsync("one", "synthetic");
        await fixture.Transaction().MutateAsync("one", null);
        Assert.Equal("{}", await File.ReadAllTextAsync(fixture.Vault));
        File.Delete(fixture.Vault);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction().MutateAsync("new", "synthetic"));
        Assert.False(File.Exists(fixture.Vault));
    }

    [Fact]
    public async Task Ownership_wait_is_bounded_and_cancellable_and_never_deletes_lock()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Protected, "secrets.local.lock");
        await using var external = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction(timeout: TimeSpan.FromMilliseconds(60)).MutateAsync("one", "synthetic"));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Transaction().MutateAsync("one", "synthetic", cancel.Token));
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(fixture.Vault));
    }

    [Fact]
    public async Task Discovery_overflow_requires_explicit_selection_without_partial_results()
    {
        using var fixture = new Fixture();
        for (var i = 0; i < 65; i++)
            await File.WriteAllTextAsync(Path.Combine(fixture.Protected, i.ToString("x64") + ".pre-v3.locator"), "synthetic-invalid");
        var exception = await Assert.ThrowsAsync<FallbackVaultTransaction.RecoveryDiscoveryLimitException>(() => fixture.Transaction().DiscoverRecoveryAsync());
        Assert.Equal("Recovery discovery requires an explicit original vault ID.", exception.Message);
    }

    [Fact]
    public async Task Locator_only_interruption_is_unavailable_and_never_repaired_by_selecting_another_snapshot()
    {
        using var fixture = new Fixture();
        fixture.Legacy("old", "synthetic-before");
        await fixture.Transaction().MutateAsync("old", "synthetic-after");
        var candidate = Assert.Single(await fixture.Transaction().DiscoverRecoveryAsync());
        File.Delete(Assert.Single(Directory.GetFiles(fixture.Protected, "*.snapshot")));
        var primary = await File.ReadAllBytesAsync(fixture.Vault);
        Assert.False(Assert.Single(await fixture.Transaction().DiscoverRecoveryAsync()).Available);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction().ReviewRecoveryAsync(candidate.OriginalId));
        Assert.Equal(primary, await File.ReadAllBytesAsync(fixture.Vault));
    }

    [Fact]
    public async Task Reviewed_recovery_refuses_locator_change_and_another_target_path()
    {
        using var fixture = new Fixture();
        fixture.Legacy("old", "synthetic-before");
        await fixture.Transaction().MutateAsync("old", "synthetic-after");
        var candidate = Assert.Single(await fixture.Transaction().DiscoverRecoveryAsync());
        var review = await fixture.Transaction().ReviewRecoveryAsync(candidate.OriginalId);
        var differentTarget = Path.Combine(fixture.Root, "different-root", "secrets.local.json");
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction(path: differentTarget).RestoreAsync(review));
        Assert.False(File.Exists(differentTarget));
        var locator = Assert.Single(Directory.GetFiles(fixture.Protected, "*.locator"));
        var contents = await File.ReadAllBytesAsync(locator);
        contents[^2] ^= 1;
        await File.WriteAllBytesAsync(locator, contents);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction().RestoreAsync(review));
        Assert.Equal("synthetic-after", await fixture.Transaction().ResolveAsync("old"));
    }

    [Fact]
    public async Task Missing_key_after_migrated_last_removal_does_not_generate_replacement_key()
    {
        using var fixture = new Fixture();
        fixture.Legacy("old", "synthetic-before");
        await fixture.Transaction().MutateAsync("old", null);
        Assert.Equal("{}", await File.ReadAllTextAsync(fixture.Vault));
        File.Delete(Path.Combine(fixture.Protected, "secrets.local.key"));
        var primary = await File.ReadAllBytesAsync(fixture.Vault);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction().MutateAsync("new", "synthetic"));
        Assert.Equal(primary, await File.ReadAllBytesAsync(fixture.Vault));
        Assert.False(File.Exists(Path.Combine(fixture.Protected, "secrets.local.key")));
        // A new target sharing that protected key directory must also preserve the history.
        var differentTarget = Path.Combine(fixture.Root, "other-root", "secrets.local.json");
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction(path: differentTarget).MutateAsync("new", "synthetic"));
        Assert.False(File.Exists(differentTarget));
    }

    [Fact]
    public async Task Existing_empty_primary_without_key_needs_explicit_initialisation_review()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Vault, "{}");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fixture.Vault, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction().MutateAsync("new", "synthetic"));
        Assert.Equal("{}", await File.ReadAllTextAsync(fixture.Vault));
        Assert.False(File.Exists(Path.Combine(fixture.Protected, "secrets.local.key")));
    }

    [Fact]
    public async Task Legacy_key_is_copied_and_verified_under_ownership_before_old_copy_is_removed()
    {
        using var fixture = new Fixture();
        fixture.Legacy("old", "synthetic-before");
        var oldPath = Path.Combine(Path.GetDirectoryName(fixture.Vault)!, "secrets.local.key");
        var keyBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Fixture.WritePrivate(oldPath, keyBytes);
        Assert.Equal("synthetic-before", await fixture.Transaction(legacyKeyPath: oldPath).ResolveAsync("old"));
        Assert.False(File.Exists(oldPath));
        Assert.Equal(keyBytes, await File.ReadAllBytesAsync(Path.Combine(fixture.Protected, "secrets.local.key")));
    }

    [Fact]
    public async Task Different_existing_legacy_and_protected_keys_are_preserved_and_refused()
    {
        using var fixture = new Fixture();
        fixture.Legacy("old", "synthetic-before");
        var oldPath = Path.Combine(Path.GetDirectoryName(fixture.Vault)!, "secrets.local.key");
        var protectedPath = Path.Combine(fixture.Protected, "secrets.local.key");
        var oldBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var protectedBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Fixture.WritePrivate(oldPath, oldBytes);
        Fixture.WritePrivate(protectedPath, protectedBytes);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction(legacyKeyPath: oldPath).ResolveAsync("old"));
        Assert.Equal(oldBytes, await File.ReadAllBytesAsync(oldPath));
        Assert.Equal(protectedBytes, await File.ReadAllBytesAsync(protectedPath));
    }

    [Fact]
    public async Task Legacy_key_copy_interruption_keeps_original_and_retry_verifies_matching_copies()
    {
        using var fixture = new Fixture();
        fixture.Legacy("old", "synthetic-before");
        var oldPath = Path.Combine(Path.GetDirectoryName(fixture.Vault)!, "secrets.local.key");
        var keyBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Fixture.WritePrivate(oldPath, keyBytes);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction(legacyKeyPath: oldPath,
            fault: phase => { if (phase == "legacy-key-replaced") throw new IOException("synthetic fault"); }).ResolveAsync("old"));
        Assert.Equal(keyBytes, await File.ReadAllBytesAsync(oldPath));
        Assert.Equal(keyBytes, await File.ReadAllBytesAsync(Path.Combine(fixture.Protected, "secrets.local.key")));
        Assert.Equal("synthetic-before", await fixture.Transaction(legacyKeyPath: oldPath).ResolveAsync("old"));
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    public async Task Invalid_legacy_key_never_replaces_or_creates_protected_key()
    {
        using var fixture = new Fixture();
        fixture.Legacy("old", "synthetic-before");
        var oldPath = Path.Combine(Path.GetDirectoryName(fixture.Vault)!, "secrets.local.key");
        Fixture.WritePrivate(oldPath, Encoding.ASCII.GetBytes(Convert.ToBase64String(new byte[31])));
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction(legacyKeyPath: oldPath).ResolveAsync("old"));
        Assert.True(File.Exists(oldPath));
        Assert.False(File.Exists(Path.Combine(fixture.Protected, "secrets.local.key")));
    }

    [Fact]
    public async Task Missing_protected_key_with_recovery_history_never_imports_an_ambiguous_legacy_key()
    {
        using var fixture = new Fixture();
        fixture.Legacy("old", "synthetic-before");
        await fixture.Transaction().MutateAsync("old", "synthetic-after");
        var history = Directory.GetFiles(fixture.Protected, "*.pre-v3.*")
            .ToDictionary(path => path, File.ReadAllBytes);
        var primary = await File.ReadAllBytesAsync(fixture.Vault);
        File.Delete(Path.Combine(fixture.Protected, "secrets.local.key"));
        var oldPath = Path.Combine(Path.GetDirectoryName(fixture.Vault)!, "secrets.local.key");
        var ambiguous = Encoding.ASCII.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Fixture.WritePrivate(oldPath, ambiguous);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction(legacyKeyPath: oldPath).ResolveAsync("old"));
        Assert.Equal(ambiguous, await File.ReadAllBytesAsync(oldPath));
        Assert.Equal(primary, await File.ReadAllBytesAsync(fixture.Vault));
        foreach (var pair in history) Assert.Equal(pair.Value, await File.ReadAllBytesAsync(pair.Key));
        Assert.False(File.Exists(Path.Combine(fixture.Protected, "secrets.local.key")));
    }

    [Fact]
    public async Task Fresh_v3_without_history_never_imports_fake_legacy_key_after_original_key_loss()
    {
        using var fixture = new Fixture();
        await fixture.Transaction().MutateAsync("current", "synthetic-v3");
        Assert.Empty(Directory.GetFiles(fixture.Protected, "*.pre-v3.*"));
        var primary = await File.ReadAllBytesAsync(fixture.Vault);
        File.Delete(Path.Combine(fixture.Protected, "secrets.local.key"));
        var oldPath = Path.Combine(Path.GetDirectoryName(fixture.Vault)!, "secrets.local.key");
        var guessed = Encoding.ASCII.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Fixture.WritePrivate(oldPath, guessed);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction(legacyKeyPath: oldPath).ResolveAsync("current"));
        Assert.Equal(primary, await File.ReadAllBytesAsync(fixture.Vault));
        Assert.Equal(guessed, await File.ReadAllBytesAsync(oldPath));
        Assert.False(File.Exists(Path.Combine(fixture.Protected, "secrets.local.key")));
        Assert.Empty(Directory.GetFiles(fixture.Protected, "*.pre-v3.*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_v2_candidate_is_validated_before_key_relocation(bool matching)
    {
        using var fixture = new Fixture();
        var originalKey = new byte[32];
        var candidateKey = matching ? originalKey : Enumerable.Repeat((byte)1, 32).ToArray();
        var salt = new byte[16];
        using var aes = Aes.Create();
        aes.Key = Rfc2898DeriveBytes.Pbkdf2(originalKey, salt, 100_000, HashAlgorithmName.SHA256, 32);
        aes.IV = new byte[16];
        var ciphertext = aes.EncryptCbc(Encoding.UTF8.GetBytes("synthetic-before"), aes.IV);
        var payload = "v2:" + Convert.ToBase64String(salt.Concat(aes.IV).Concat(ciphertext).ToArray());
        var primary = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["old"] = payload });
        Fixture.WritePrivate(fixture.Vault, primary);
        var oldPath = Path.Combine(Path.GetDirectoryName(fixture.Vault)!, "secrets.local.key");
        var keyBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(candidateKey));
        Fixture.WritePrivate(oldPath, keyBytes);
        var transaction = fixture.Transaction(legacyKeyPath: oldPath, legacyDecoder: (value, key) =>
        {
            var envelope = Convert.FromBase64String(value[3..]);
            using var decoder = Aes.Create();
            decoder.Key = Rfc2898DeriveBytes.Pbkdf2(key, envelope.AsSpan(0, 16), 100_000, HashAlgorithmName.SHA256, 32);
            return new UTF8Encoding(false, true).GetString(decoder.DecryptCbc(envelope.AsSpan(32), envelope.AsSpan(16, 16)));
        });
        if (matching)
        {
            Assert.Equal("synthetic-before", await transaction.ResolveAsync("old"));
            Assert.Equal(keyBytes, await File.ReadAllBytesAsync(Path.Combine(fixture.Protected, "secrets.local.key")));
            Assert.False(File.Exists(oldPath));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => transaction.ResolveAsync("old"));
            Assert.Equal(keyBytes, await File.ReadAllBytesAsync(oldPath));
            Assert.False(File.Exists(Path.Combine(fixture.Protected, "secrets.local.key")));
        }
        Assert.Equal(primary, await File.ReadAllBytesAsync(fixture.Vault));
        Assert.Empty(Directory.GetFiles(fixture.Protected, "*.pre-v3.*"));
    }

    [Theory]
    [InlineData("v99:unsupported")]
    [InlineData("invalid base64")]
    public async Task Invalid_sibling_prevents_legacy_key_publication_before_resolving_a_valid_entry(string invalid)
    {
        using var fixture = new Fixture();
        var primary = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        {
            ["requested"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("synthetic-before")), ["invalid"] = invalid
        });
        Fixture.WritePrivate(fixture.Vault, primary);
        var oldPath = Path.Combine(Path.GetDirectoryName(fixture.Vault)!, "secrets.local.key");
        var keyBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Fixture.WritePrivate(oldPath, keyBytes);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction(legacyKeyPath: oldPath).ResolveAsync("requested"));
        Assert.Equal(primary, await File.ReadAllBytesAsync(fixture.Vault));
        Assert.Equal(keyBytes, await File.ReadAllBytesAsync(oldPath));
        Assert.False(File.Exists(Path.Combine(fixture.Protected, "secrets.local.key")));
        Assert.Empty(Directory.GetFiles(fixture.Protected, "*.pre-v3.*"));
    }

    [Fact]
    public async Task Transition_admission_is_rechecked_before_primary_commit_and_restore()
    {
        using var fixture = new Fixture();
        fixture.Legacy("old", "synthetic-before");
        var original = await File.ReadAllBytesAsync(fixture.Vault);
        var blocked = false;
        Action admission = () => { if (blocked) throw new InvalidOperationException("synthetic transition"); };
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction(validateOperation: admission,
            fault: phase => { if (phase == "primary-flushed") blocked = true; }).MutateAsync("old", "synthetic-after"));
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Vault));
        blocked = false;
        await fixture.Transaction().MutateAsync("old", "synthetic-after");
        var id = Assert.Single(await fixture.Transaction().DiscoverRecoveryAsync()).OriginalId;
        var review = await fixture.Transaction().ReviewRecoveryAsync(id);
        var current = await File.ReadAllBytesAsync(fixture.Vault);
        await Assert.ThrowsAsync<IOException>(() => fixture.Transaction(validateOperation: admission,
            fault: phase => { if (phase == "restore-flushed") blocked = true; }).RestoreAsync(review));
        Assert.Equal(current, await File.ReadAllBytesAsync(fixture.Vault));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Protected_directory_equal_to_or_beneath_Data_Root_is_refused_before_writes(bool nested)
    {
        using var temp = new TempDir();
        var root = temp.PathFor("data");
        var protectedDirectory = nested ? Path.Combine(root, "protected") : root;

        var failure = Assert.Throws<ArgumentException>(() => new FallbackVaultTransaction(
            Path.Combine(root, "secrets.local.json"), protectedDirectory,
            static (_, _) => throw new InvalidOperationException("Unexpected legacy decoding.")));

        Assert.Equal("protectedDirectory", failure.ParamName);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Protected_directory_case_alias_uses_platform_path_identity()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("caseroot");
        var protectedDirectory = temp.PathFor("CaseRoot");
        FallbackVaultTransaction Create() => new(Path.Combine(root, "secrets.local.json"), protectedDirectory,
            static (_, _) => throw new InvalidOperationException("Unexpected legacy decoding."));
        if (OperatingSystem.IsWindows())
        {
            Assert.Throws<ArgumentException>(Create);
            Assert.False(Directory.Exists(root));
        }
        else
        {
            var transaction = Create();
            await transaction.MutateAsync("name", "synthetic");
            Assert.Equal("synthetic", await transaction.ResolveAsync("name"));
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "hermaeus-vault-transaction-" + Guid.NewGuid().ToString("N"));
        internal string Protected => Path.Combine(Root, "protected");
        internal string Vault => Path.Combine(Root, "data", "secrets.local.json");
        internal Fixture()
        {
            Directory.CreateDirectory(Protected);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(Protected, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.CreateDirectory(Path.GetDirectoryName(Vault)!);
        }
        internal FallbackVaultTransaction Transaction(string? path = null, Action<string>? fault = null, TimeSpan? timeout = null,
            string? legacyKeyPath = null, Action? validateOperation = null, Func<string, byte[], string>? legacyDecoder = null) =>
            new(path ?? Vault, Protected, legacyDecoder ?? LegacyDecode, timeout, fault, legacyKeyPath, validateOperation);
        internal static void WritePrivate(string path, byte[] bytes)
        {
            File.WriteAllBytes(path, bytes);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        internal byte[] Legacy(string name, string value)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { [name] = Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) });
            File.WriteAllBytes(Vault, bytes);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Vault, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return bytes;
        }
        private static string LegacyDecode(string value, byte[] key)
        {
            _ = key;
            return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(value));
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
