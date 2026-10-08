using System.Text.Json.Serialization;

namespace Hermaeus.Core.Models;

/// <summary>
/// The small, data-only manifest understood by the ChatGPT Pet v2 package
/// format. Packages contain no executable hooks. The desktop host owns image
/// decoding and animation, while Services owns path and resource validation.
/// </summary>
public sealed class PetPackageManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("spriteVersionNumber")]
    public int SpriteVersionNumber { get; set; }

    [JsonPropertyName("spritesheetPath")]
    public string SpritesheetPath { get; set; } = string.Empty;
}

public sealed record PetPackage(
    string RootDirectory,
    PetPackageManifest Manifest,
    string SpritesheetPath,
    bool IsBundled);

public sealed record PetPackageResult(
    bool Succeeded,
    PetPackage? Package,
    string Error)
{
    public static PetPackageResult Failure(string error) => new(false, null, error);
}
