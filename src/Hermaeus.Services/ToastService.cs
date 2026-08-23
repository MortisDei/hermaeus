using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class ToastService : IToastService
{
    public event Action<ToastMessage>? ToastRaised;

    public void Show(string title, string message, ToastKind kind = ToastKind.Info, int durationMs = 3500) =>
        ToastRaised?.Invoke(new ToastMessage(title, message, kind, durationMs));

    public void ShowDetails(string title, string message, ToastKind kind = ToastKind.Info, int durationMs = 7000) =>
        ToastRaised?.Invoke(new ToastMessage(title, message, kind, durationMs, CanCopyDetails: true));
}
