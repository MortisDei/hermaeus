using System.IO.Compression;
using System.Text;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class BackupSecretArtifactTests
{
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private const string VaultId = Nonce + Nonce;
    private static readonly string[] PrivateNames =
    [
        "secrets.local.json", "secrets.local.key", "secrets.local.lock",
        "secrets.local.json." + Nonce + ".tmp",
        "secrets.local.key." + Nonce + ".tmp",
        ".vault-" + Nonce + ".tmp",
        VaultId + ".pre-v3.snapshot", VaultId + ".pre-v3.locator"
    ];
    private static readonly string[] OrdinaryNames =
    [
        "notes.txt", ".vault-not-a-guid.tmp", "secrets.local.json.notes",
        "secrets.local.key.not-a-guid.tmp", "report.pre-v3.snapshot",
        VaultId + ".pre-v3.snapshot.notes"
    ];

    [Fact]
    public async Task Backup_excludes_current_credential_artifacts_at_root_and_in_nested_directories()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var root = SettingsService.ResolveDataRoot(settings.Settings);
        var privatePaths = new List<string>();
        foreach (var directory in new[] { root, Path.Combine(root, "nested") })
        {
            Directory.CreateDirectory(directory);
            foreach (var name in PrivateNames)
            {
                var path = Path.Combine(directory, name);
                // Invalid or crash-leftover credential material is private too.
                await File.WriteAllTextAsync(path, "synthetic-sensitive-artifact");
                privatePaths.Add(path);
            }
        }
        foreach (var name in OrdinaryNames)
            await File.WriteAllTextAsync(Path.Combine(root, name), "ordinary owner data");

        var backup = await new BackupService(settings).BackupAsync(temp.PathFor("backups"));

        using var archive = ZipFile.OpenRead(backup.Path);
        Assert.Equal(OrdinaryNames.Order(StringComparer.Ordinal).ToArray(), archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(OrdinaryNames.Length, backup.FilesIncluded);
        foreach (var path in privatePaths)
            Assert.Equal("synthetic-sensitive-artifact", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("secrets.local.json")]
    [InlineData("nested/secrets.local.key")]
    [InlineData("secrets.local.lock")]
    [InlineData("secrets.local.json." + Nonce + ".tmp")]
    [InlineData("nested/secrets.local.key." + Nonce + ".tmp")]
    [InlineData(".vault-" + Nonce + ".tmp")]
    [InlineData(VaultId + ".pre-v3.snapshot")]
    [InlineData("nested/" + VaultId + ".pre-v3.locator")]
    [InlineData("nested\\secrets.local.json")]
    [InlineData("./secrets.local.json")]
    [InlineData("secrets.local.json/.")]
    [InlineData("SECRETS.LOCAL.JSON")]
    public async Task Restore_refuses_reserved_credential_targets_before_publishing_any_files(string name)
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var root = SettingsService.ResolveDataRoot(settings.Settings);
        Directory.CreateDirectory(root);
        var notes = Path.Combine(root, "notes.txt");
        var vault = Path.Combine(root, "secrets.local.json");
        var key = Path.Combine(root, "secrets.local.key");
        await File.WriteAllTextAsync(notes, "owner notes");
        await File.WriteAllTextAsync(vault, "synthetic existing vault");
        await File.WriteAllTextAsync(key, "synthetic existing key");
        var archivePath = temp.PathFor("untrusted.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "notes.txt", "replacement notes");
            WriteEntry(archive, name, "synthetic credential replacement");
        }

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService(settings)
            .RestoreAsync(archivePath, allowOverwrite: true));

        Assert.Contains("credential", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("owner notes", await File.ReadAllTextAsync(notes));
        Assert.Equal("synthetic existing vault", await File.ReadAllTextAsync(vault));
        Assert.Equal("synthetic existing key", await File.ReadAllTextAsync(key));
        Assert.Equal(3, Directory.GetFiles(root).Length);
        Assert.Empty(Directory.GetDirectories(root));
    }

    [Fact]
    public async Task Ordinary_similar_names_remain_restorable()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var archivePath = temp.PathFor("ordinary.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            foreach (var name in OrdinaryNames) WriteEntry(archive, name, "ordinary owner data");

        await new BackupService(settings).RestoreAsync(archivePath);

        var root = SettingsService.ResolveDataRoot(settings.Settings);
        foreach (var name in OrdinaryNames)
            Assert.Equal("ordinary owner data", await File.ReadAllTextAsync(Path.Combine(root, name)));
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
