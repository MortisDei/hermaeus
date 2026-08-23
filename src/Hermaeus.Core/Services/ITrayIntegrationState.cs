namespace Hermaeus.Core.Services;

/// <summary>
/// Current-session evidence reported by the desktop tray integration.
/// Creating a tray object is not proof that the desktop environment displayed
/// it; a user interaction is.
/// </summary>
public interface ITrayIntegrationState
{
    bool IsConfirmed { get; }
    void Confirm();
}
