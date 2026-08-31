using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

/// <summary>
/// Adapter for a resident in-process model session. It reports no allocation
/// before the session is loaded and reports an explicit Unknown byte value once
/// loaded unless a trustworthy measurement is supplied by a later adapter.
/// </summary>
public sealed class InProcessResourceConsumerAdapter : IResourceConsumerAdapter
{
    private readonly Func<bool> _isLoaded;
    private readonly ResourceComponentKind _componentKind;

    public ResourceConsumerDescriptor Descriptor { get; }

    public InProcessResourceConsumerAdapter(
        ResourceConsumerDescriptor descriptor,
        Func<bool> isLoaded,
        ResourceComponentKind componentKind = ResourceComponentKind.OnnxSession)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _isLoaded = isLoaded ?? throw new ArgumentNullException(nameof(isLoaded));
        _componentKind = componentKind;
    }

    public Task<ResourceAllocation?> CaptureAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_isLoaded())
            return Task.FromResult<ResourceAllocation?>(null);

        var allocation = new ResourceAllocation(
            $"{Descriptor.ConsumerId}:allocation",
            Descriptor.ConsumerId,
            null,
            ResourceLifecycleState.Active,
            null,
            [],
            null,
            null,
            [new ResourceAllocationComponent(
                $"{Descriptor.ConsumerId}:session",
                _componentKind,
                null,
                null,
                null,
                null,
                ResourceEvidenceState.Unknown)],
            DateTime.UtcNow,
            []);
        return Task.FromResult<ResourceAllocation?>(allocation);
    }
}

public static class ResourceConsumerAdapters
{
    public static IResourceConsumerAdapter Reranker(Func<bool> isLoaded) =>
        InProcess(
            "rag.reranker",
            ResourceConsumerKind.Reranker,
            "OnnxCrossEncoderReranker",
            ResourcePriorityClass.Background,
            ResourceReclaimability.Cooperative,
            isLoaded);

    public static IResourceConsumerAdapter Whisper(Func<bool> isLoaded) =>
        InProcess(
            "stt.whisper",
            ResourceConsumerKind.SpeechToText,
            "NativeSpeechRecognitionProvider",
            ResourcePriorityClass.Foreground,
            ResourceReclaimability.Cooperative,
            isLoaded);

    public static IResourceConsumerAdapter Kokoro(Func<bool> isLoaded) =>
        InProcess(
            "tts.kokoro",
            ResourceConsumerKind.TextToSpeech,
            "NativeKokoroVoiceProvider",
            ResourcePriorityClass.Foreground,
            ResourceReclaimability.Cooperative,
            isLoaded);

    private static IResourceConsumerAdapter InProcess(
        string consumerId,
        ResourceConsumerKind kind,
        string lifecycleService,
        ResourcePriorityClass priority,
        ResourceReclaimability reclaimability,
        Func<bool> isLoaded) => new InProcessResourceConsumerAdapter(
        new ResourceConsumerDescriptor(
            consumerId,
            kind,
            ResourceOwnerIdentity.InProcess(consumerId),
            lifecycleService,
            priority,
            reclaimability,
            [ResourceKind.DeviceMemory, ResourceKind.SystemResidentMemory]),
        isLoaded);
}
