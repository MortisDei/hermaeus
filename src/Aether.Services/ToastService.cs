using Aether.Core.Services;

namespace Aether.Services;

public sealed class ToastService : IToastService
{
    public event Action<ToastMessage>? ToastRaised;

    public void Show(string title, string message, ToastKind kind = ToastKind.Info, int durationMs = 3500) =>
        ToastRaised?.Invoke(new ToastMessage(title, message, kind, durationMs));
}
