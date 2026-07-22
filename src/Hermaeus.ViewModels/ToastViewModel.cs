using Hermaeus.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hermaeus.ViewModels;

public partial class ToastViewModel : ObservableObject
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public ToastKind Kind { get; init; }
    public int DurationMs { get; init; } = 3500;

    public string KindLabel => Kind switch
    {
        ToastKind.Success => "Success",
        ToastKind.Warning => "Warning",
        ToastKind.Error   => "Error",
        _                 => "Info"
    };
}
