namespace Hermaeus.Core.Services;

public enum ToastKind { Info, Success, Warning, Error }

public sealed record ToastMessage(
    string Title,
    string Message,
    ToastKind Kind = ToastKind.Info,
    int DurationMs = 3500,
    bool CanCopyDetails = false);

public interface IToastService
{
    event Action<ToastMessage>? ToastRaised;
    void Show(string title, string message, ToastKind kind = ToastKind.Info, int durationMs = 3500);
    void ShowDetails(string title, string message, ToastKind kind = ToastKind.Info, int durationMs = 7000) =>
        Show(title, message, kind, durationMs);
}
