namespace Aether.Core.Services;

public enum ToastKind { Info, Success, Warning, Error }

public sealed record ToastMessage(
    string Title,
    string Message,
    ToastKind Kind = ToastKind.Info,
    int DurationMs = 3500);

public interface IToastService
{
    event Action<ToastMessage>? ToastRaised;
    void Show(string title, string message, ToastKind kind = ToastKind.Info, int durationMs = 3500);
}
