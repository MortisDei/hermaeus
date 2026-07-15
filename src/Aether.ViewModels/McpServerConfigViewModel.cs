using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public sealed partial class McpServerConfigViewModel : ObservableObject
{
    public McpServerConfigViewModel(McpServerConfig config)
    {
        Id = config.Id;
        _name = config.Name;
        _command = config.Command;
        _argumentsText = string.Join(' ', config.Arguments);
        _workingDirectory = config.WorkingDirectory;
        _enabled = config.Enabled;
        _allowedToolsText = string.Join(", ", config.AllowedTools);
    }

    public string Id { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _command;
    [ObservableProperty] private string _argumentsText;
    [ObservableProperty] private string _workingDirectory;
    [ObservableProperty] private bool _enabled;

    /// <summary>
    /// Comma-separated tool names; empty means no restriction (every tool the
    /// server declares is callable), matching prior behavior
    /// (docs/review/03-next-level-roadmap.md Phase 3).
    /// </summary>
    [ObservableProperty] private string _allowedToolsText;

    public McpServerConfig ToConfig() => new()
    {
        Id = Id,
        Name = Name,
        Command = Command,
        Arguments = ArgumentsText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        WorkingDirectory = WorkingDirectory,
        Enabled = Enabled,
        AllowedTools = AllowedToolsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
    };
}
