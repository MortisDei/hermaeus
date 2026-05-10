using Aether.Core.Models;

namespace Aether.Core.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    Task LoadAsync();
    Task SaveAsync();
    event EventHandler? SettingsChanged;
}
