namespace Hermaeus.Core.Models;

public enum VoiceChannel
{
    Chat,
    Agent,
    Doctor,
    Benchmark,
    Notification,
    System
}

public enum VoicePriority
{
    Low,
    Normal,
    Critical
}

public sealed record VoiceUtterance(
    string Text,
    VoiceChannel Channel,
    VoicePriority Priority = VoicePriority.Normal,
    string? VoiceOverride = null,
    string? DedupeKey = null);
