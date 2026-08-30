namespace Hermaeus.Core.Models;

public enum AudioFeedbackEventKind
{
    TaskNeedsApproval,
    TaskCompleted,
    TaskFailed,
    ManagedRuntimeReady,
    ManagedRuntimeFailed,
    LongOperationCompleted,
    RecordingStarted,
    RecordingStopped
}

public sealed class AudioFeedbackSettings
{
    public bool Enabled { get; set; } = true;
    public int Volume { get; set; } = 50;
    public bool Muted { get; set; }
    public bool SuppressWhileTtsSpeaking { get; set; } = true;
    public Dictionary<string, bool> EventEnabled { get; set; } = [];

    public bool IsEnabled(AudioFeedbackEventKind kind)
    {
        if (EventEnabled.TryGetValue(kind.ToString(), out var enabled))
            return enabled;
        return kind is AudioFeedbackEventKind.ManagedRuntimeFailed;
    }
}
