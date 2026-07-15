using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class McpSettingsViewModel : ObservableObject
{
    public ObservableCollection<McpServerConfigViewModel> Servers { get; } = [];

    public void ReloadFrom(AppSettings settings)
    {
        Servers.Clear();
        foreach (var server in settings.Mcp.Servers)
            Servers.Add(new McpServerConfigViewModel(server));
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.Mcp.Servers = Servers.Select(s => s.ToConfig()).ToList();
    }

    [RelayCommand]
    private void AddServer() => Servers.Add(new McpServerConfigViewModel(new McpServerConfig { Name = "New MCP server" }));

    [RelayCommand]
    private void RemoveServer(McpServerConfigViewModel? item)
    {
        if (item is not null) Servers.Remove(item);
    }
}
