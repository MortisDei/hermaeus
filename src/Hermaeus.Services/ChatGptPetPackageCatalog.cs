using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

/// <summary>
/// Safe local catalog for ChatGPT Pet v2 packages. A package is data only:
/// the manifest and a bounded set of image/metadata files are accepted, all
/// paths remain inside the package directory, and symbolic links are rejected.
/// </summary>
public sealed class ChatGptPetPackageCatalog : IChatGptPetPackageCatalog
{
    public const int SpriteVersionNumber = 2;
    public const int MaxManifestBytes = 1024 * 1024;
    public const int MaxPackageFiles = 64;
    public const long MaxPackageBytes = 50L * 1024 * 1024;
    public const long MaxSingleFileBytes = 50L * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".webp", ".png", ".jpg", ".jpeg", ".gif", ".txt", ".md", ".license", ".licence"
    };

    private static readonly HashSet<string> RejectedExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".com", ".dll", ".dylib", ".exe", ".js", ".mjs", ".ps1", ".py", ".sh", ".so", ".wasm"
    };

    private readonly ISettingsService _settings;
    private readonly string _bundledRoot;

    public ChatGptPetPackageCatalog(ISettingsService settings)
        : this(settings, Path.Combine(AppContext.BaseDirectory, "Pets"))
    {
    }

    internal ChatGptPetPackageCatalog(ISettingsService settings, string bundledRoot)
    {
        _settings = settings;
        _bundledRoot = Path.GetFullPath(bundledRoot);
    }

    public IReadOnlyList<ChatGptPetPackage> GetAvailablePackages()
    {
        var packages = new Dictionary<string, ChatGptPetPackage>(StringComparer.OrdinalIgnoreCase);
        AddPackagesFromRoot(ResolveUserRoot(), isBundled: false, packages);
        AddPackagesFromRoot(_bundledRoot, isBundled: true, packages);
        return packages.Values
            .OrderBy(package => package.Manifest.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<ChatGptPetPackageResult> ImportAsync(
        string manifestPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ImportCore(manifestPath, cancellationToken), cancellationToken);

    private ChatGptPetPackageResult ImportCore(string manifestPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(manifestPath))
            return ChatGptPetPackageResult.Failure("Choose a pet.json file.");

        string fullManifestPath;
        try
        {
            fullManifestPath = Path.GetFullPath(manifestPath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ChatGptPetPackageResult.Failure("The selected manifest path is not valid.");
        }

        if (!string.Equals(Path.GetFileName(fullManifestPath), "pet.json", StringComparison.OrdinalIgnoreCase))
            return ChatGptPetPackageResult.Failure("Select the package's pet.json manifest.");

        var sourceRoot = Path.GetDirectoryName(fullManifestPath);
        if (sourceRoot is null)
            return ChatGptPetPackageResult.Failure("The selected manifest has no package folder.");

        if (!TryReadPackage(sourceRoot, isBundled: false, out var package, out var error))
            return ChatGptPetPackageResult.Failure(error);

        var destinationRoot = ResolveUserRoot();
        var destination = Path.Combine(destinationRoot, package.Manifest.Id);
        if (Directory.Exists(destination) || File.Exists(destination))
            return ChatGptPetPackageResult.Failure($"A pet with id '{package.Manifest.Id}' is already installed.");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationRoot);
            Directory.CreateDirectory(destination);
            foreach (var sourceFile in EnumerateSafeFiles(sourceRoot, cancellationToken))
            {
                var relative = Path.GetRelativePath(sourceRoot, sourceFile);
                var target = ResolveUnderRoot(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(sourceFile, target, overwrite: false);
            }

            if (!TryReadPackage(destination, isBundled: false, out var copied, out error))
            {
                TryDeleteOwnedDirectory(destination);
                return ChatGptPetPackageResult.Failure($"The imported package did not verify: {error}");
            }

            return new ChatGptPetPackageResult(true, copied, string.Empty);
        }
        catch (OperationCanceledException)
        {
            TryDeleteOwnedDirectory(destination);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            TryDeleteOwnedDirectory(destination);
            return ChatGptPetPackageResult.Failure($"The pet package could not be imported: {ex.Message}");
        }
    }

    private void AddPackagesFromRoot(
        string root,
        bool isBundled,
        IDictionary<string, ChatGptPetPackage> packages)
    {
        if (!Directory.Exists(root) || IsReparsePoint(root))
            return;

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (IsReparsePoint(directory))
                    continue;
                if (TryReadPackage(directory, isBundled, out var package, out _))
                    packages[package.Manifest.Id] = package;
            }
        }
        catch (IOException)
        {
            // A missing or locked optional package must not prevent the app
            // from starting or make the existing bundled package disappear.
        }
        catch (UnauthorizedAccessException)
        {
            // Same fail-closed behavior for an unreadable user package root.
        }
    }

    private bool TryReadPackage(
        string packageRoot,
        bool isBundled,
        out ChatGptPetPackage package,
        out string error)
    {
        package = null!;
        error = string.Empty;
        string root;
        try
        {
            root = Path.GetFullPath(packageRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "The pet package path is not valid.";
            return false;
        }

        if (!Directory.Exists(root) || !ValidateNoReparseAncestors(root))
        {
            error = "The pet package folder is missing or is a symbolic link.";
            return false;
        }

        var manifestPath = Path.Combine(root, "pet.json");
        if (!File.Exists(manifestPath) || IsReparsePoint(manifestPath))
        {
            error = "The package must contain a regular pet.json file.";
            return false;
        }

        try
        {
            var manifestInfo = new FileInfo(manifestPath);
            if (manifestInfo.Length > MaxManifestBytes)
            {
                error = "pet.json is too large.";
                return false;
            }

            var manifest = JsonSerializer.Deserialize<ChatGptPetManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null)
            {
                error = "pet.json is empty.";
                return false;
            }

            if (!IsSafeId(manifest.Id))
            {
                error = "Pet id must use only lowercase letters, numbers, '.', '_' or '-'.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(manifest.DisplayName) || manifest.DisplayName.Length > 100)
            {
                error = "Pet displayName is required and must be at most 100 characters.";
                return false;
            }
            if (manifest.Description.Length > 1000)
            {
                error = "Pet description is too long.";
                return false;
            }
            if (manifest.SpriteVersionNumber != SpriteVersionNumber)
            {
                error = $"This package uses sprite version {manifest.SpriteVersionNumber}; Hermaeus supports version {SpriteVersionNumber}.";
                return false;
            }
            if (!TryResolveAsset(root, manifest.SpritesheetPath, out var spritesheetPath, out error)
                || !IsSpriteExtension(spritesheetPath))
            {
                error = string.IsNullOrWhiteSpace(error)
                    ? "spritesheetPath must point to a .webp or .png file inside the package."
                    : error;
                return false;
            }

            var files = EnumerateSafeFiles(root, CancellationToken.None).ToArray();
            if (files.Length > MaxPackageFiles)
            {
                error = $"The package contains more than {MaxPackageFiles} files.";
                return false;
            }

            long totalBytes = 0;
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                if (info.Length > MaxSingleFileBytes || (totalBytes += info.Length) > MaxPackageBytes)
                {
                    error = $"The package exceeds the {MaxPackageBytes / (1024 * 1024)} MiB resource limit.";
                    return false;
                }
            }

            if (!File.Exists(spritesheetPath))
            {
                error = "The package spritesheet is missing.";
                return false;
            }

            package = new ChatGptPetPackage(root, manifest, spritesheetPath, isBundled);
            return true;
        }
        catch (JsonException)
        {
            error = "pet.json is not valid JSON for a ChatGPT Pet v2 package.";
            return false;
        }
        catch (IOException)
        {
            error = "The pet package could not be read.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            error = "The pet package could not be read.";
            return false;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static IEnumerable<string> EnumerateSafeFiles(string root, CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            if (IsReparsePoint(directory))
                throw new InvalidOperationException("Symbolic links and junctions are not accepted in pet packages.");

            foreach (var child in Directory.EnumerateDirectories(directory))
                directories.Push(child);

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (IsReparsePoint(file))
                    throw new InvalidOperationException("Symbolic links and junctions are not accepted in pet packages.");
                var extension = Path.GetExtension(file);
                if (RejectedExecutableExtensions.Contains(extension) || !AllowedExtensions.Contains(extension))
                    throw new InvalidOperationException($"The pet package contains an unsupported file type: {extension}.");
                yield return file;
            }
        }
    }

    private static bool TryResolveAsset(string root, string? relativePath, out string path, out string error)
    {
        path = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            error = "spritesheetPath must be a relative path inside the package.";
            return false;
        }

        if (relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is ".." or "."))
        {
            error = "spritesheetPath cannot contain traversal segments.";
            return false;
        }

        try
        {
            path = ResolveUnderRoot(root, relativePath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or InvalidOperationException)
        {
            error = "spritesheetPath is not valid.";
            return false;
        }

        if (!File.Exists(path) || IsReparsePoint(path))
        {
            error = "The package spritesheet is missing or is a symbolic link.";
            return false;
        }
        return true;
    }

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("The pet package path escapes its package root.");
        return fullPath;
    }

    private static bool IsSafeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            return false;
        if (value[0] is '.' or '-' || value[^1] is '.' or '-')
            return false;
        return value.All(character => character is >= 'a' and <= 'z'
            or >= '0' and <= '9' or '.' or '_' or '-');
    }

    private static bool IsSpriteExtension(string path) =>
        string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);

    private static bool IsReparsePoint(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static bool ValidateNoReparseAncestors(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (IsReparsePoint(current))
                return false;
            var parent = Path.GetDirectoryName(current);
            if (parent is null || string.Equals(parent, current, StringComparison.Ordinal))
                break;
            current = parent;
        }
        return true;
    }

    private static void TryDeleteOwnedDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !IsReparsePoint(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Import failed; retaining a partial directory is safer than
            // touching a path whose ownership could no longer be proven.
        }
    }

    private string ResolveUserRoot() =>
        Path.Combine(SettingsService.ResolveDataRoot(_settings.Settings), "pets");
}
