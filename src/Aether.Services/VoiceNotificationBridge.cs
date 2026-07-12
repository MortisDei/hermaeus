using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// Bridges <see cref="IToastService"/> onto the Notification voice channel.
/// Warning toasts speak at Normal priority, Error toasts at Critical;
/// Info/Success toasts are never spoken. Registered as a singleton and
/// resolved once at startup purely to subscribe; nothing else holds a
/// reference to it.
/// </summary>
public sealed class VoiceNotificationBridge
{
    private readonly IVoiceOrchestrator _voice;

    public VoiceNotificationBridge(IToastService toasts, IVoiceOrchestrator voice)
    {
        _voice = voice;
        toasts.ToastRaised += OnToastRaised;
    }

    private void OnToastRaised(ToastMessage toast)
    {
        var priority = toast.Kind switch
        {
            ToastKind.Error => VoicePriority.Critical,
            ToastKind.Warning => VoicePriority.Normal,
            _ => (VoicePriority?)null
        };
        if (priority is null)
            return;

        _ = _voice.EnqueueAsync(new VoiceUtterance(
            $"{toast.Title}. {toast.Message}",
            VoiceChannel.Notification,
            priority.Value,
            DedupeKey: $"{toast.Title}|{toast.Message}"));
    }
}
