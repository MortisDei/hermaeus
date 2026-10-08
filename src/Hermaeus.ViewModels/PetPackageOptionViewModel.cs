using Hermaeus.Core.Models;

namespace Hermaeus.ViewModels;

public sealed class PetPackageOptionViewModel
{
    public PetPackageOptionViewModel(PetPackage package)
    {
        Id = package.Manifest.Id;
        DisplayName = package.Manifest.DisplayName;
        Description = package.Manifest.Description;
        SourceLabel = package.IsBundled ? "Bundled" : "Imported";
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string SourceLabel { get; }
    public string Display => $"{DisplayName} ({SourceLabel})";
}
