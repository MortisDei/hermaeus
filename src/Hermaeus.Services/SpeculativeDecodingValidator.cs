using Hermaeus.Core.Models;

namespace Hermaeus.Services;

public enum SpeculativeValidationSeverity
{
    /// <summary>Nothing to say; the server starts.</summary>
    Ok,

    /// <summary>A bad idea rather than a broken one. The server starts and the speed check will show it.</summary>
    Warning,

    /// <summary>The launch is doomed and is refused with the cause named, like the port-conflict refusal.</summary>
    Refusal
}

public sealed record SpeculativeValidationResult(SpeculativeValidationSeverity Severity, string Message)
{
    public static readonly SpeculativeValidationResult Fine = new(SpeculativeValidationSeverity.Ok, string.Empty);

    public static SpeculativeValidationResult Refuse(string message) => new(SpeculativeValidationSeverity.Refusal, message);
    public static SpeculativeValidationResult Warn(string message) => new(SpeculativeValidationSeverity.Warning, message);

    public bool IsRefusal => Severity == SpeculativeValidationSeverity.Refusal;
    public bool HasMessage => Message.Length > 0;
}

/// <summary>
/// r27 03-drafting-and-proof.md 3.3: an incompatible draft model is refused
/// before launch rather than producing a server that starts and then fails, or
/// worse, one that runs and silently costs performance.
/// This is the part r18 4.4 was right to be afraid of. An MTP head shares its
/// base model's vocabulary by construction, so the compatibility question does
/// not arise for the case the owner will actually run; it very much does for an
/// arbitrary small model pointed at an arbitrary large one.
/// Pure over a <see cref="ServerConfig"/> and the filesystem: no process, no
/// network, no new package.
/// </summary>
public static class SpeculativeDecodingValidator
{
    /// <summary>A draft larger than this fraction of its target usually loses to verification overhead.</summary>
    private const double DraftSizeWarnRatio = 0.5;

    public static SpeculativeValidationResult Validate(ServerConfig cfg)
    {
        var speculative = cfg.Speculative;
        if (speculative is null || !speculative.RequiresDraftModel)
            return SpeculativeValidationResult.Fine;

        var draftPath = speculative.DraftModelPath?.Trim() ?? string.Empty;
        var types = string.Join(", ", speculative.Types);

        if (draftPath.Length == 0)
            return SpeculativeValidationResult.Refuse(
                $"Speculative decoding is set to {types}, which needs a draft model, but no draft model file is selected.");

        // Same shape of check the app applies to every other user-controlled
        // path: no traversal segments, must exist, never a link.
        if (draftPath.Split(['/', '\\']).Any(segment => segment == ".."))
            return SpeculativeValidationResult.Refuse($"The draft model path cannot contain '..' segments: '{draftPath}'.");

        string fullDraft;
        try
        {
            fullDraft = Path.GetFullPath(draftPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return SpeculativeValidationResult.Refuse($"The draft model path is not valid: '{draftPath}'.");
        }

        if (!File.Exists(fullDraft))
            return SpeculativeValidationResult.Refuse($"The draft model file does not exist: '{fullDraft}'.");

        try
        {
            if (File.GetAttributes(fullDraft).HasFlag(FileAttributes.ReparsePoint))
                return SpeculativeValidationResult.Refuse($"The draft model path cannot be a symbolic link or junction: '{fullDraft}'.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SpeculativeValidationResult.Refuse($"The draft model file could not be read: '{fullDraft}'.");
        }

        var targetPath = cfg.ModelPath?.Trim() ?? string.Empty;
        if (targetPath.Length == 0 || !File.Exists(targetPath))
            return SpeculativeValidationResult.Fine;

        // A mismatch names both models and both sizes, so the message says what
        // to do rather than only that something is wrong.
        var target = GgufMetadataReader.TryRead(targetPath);
        var draft = GgufMetadataReader.TryRead(fullDraft);
        if (target?.VocabularySize is > 0 && draft?.VocabularySize is > 0
            && target.VocabularySize != draft.VocabularySize)
        {
            return SpeculativeValidationResult.Refuse(
                $"The draft model's vocabulary does not match the target model's, so speculative decoding cannot verify its tokens. " +
                $"{Path.GetFileName(targetPath)} has {target.VocabularySize:N0} tokens; " +
                $"{Path.GetFileName(fullDraft)} has {draft.VocabularySize:N0}. " +
                $"Use a draft trained for this model, such as its MTP head.");
        }

        var targetBytes = new FileInfo(targetPath).Length;
        var draftBytes = new FileInfo(fullDraft).Length;
        if (targetBytes > 0 && draftBytes > targetBytes * DraftSizeWarnRatio)
        {
            return SpeculativeValidationResult.Warn(
                $"The draft model is {FormatSize(draftBytes)} against a {FormatSize(targetBytes)} target. " +
                $"A draft that large usually loses to the cost of verifying it. Run the speed check to see whether it helps here.");
        }

        return SpeculativeValidationResult.Fine;
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024d / 1024d / 1024d:0.#} GB"
            : $"{bytes / 1024d / 1024d:0.#} MB";
}

public sealed record SpeculativePairInspection(
    string Mechanism,
    CapabilityState State,
    string Detail,
    ModelIdentityV2 TargetIdentity,
    ModelIdentityV2 CompanionIdentity,
    string TargetArchitecture,
    string CompanionArchitecture,
    string TokenizerIdentity,
    int? TargetVocabularySize,
    int? CompanionVocabularySize,
    long TargetBytes,
    long CompanionBytes);

/// <summary>
/// Lab-only compatibility gate for a specific target, companion, and runtime
/// mechanism. It is intentionally stricter than the normal Services advisory:
/// Lab cannot run an unproven pair and label the result controlled.
/// </summary>
public static class SpeculativePairInspector
{
    public static SpeculativePairInspection Inspect(string mechanism, ServerConfig source,
        IReadOnlyCollection<RuntimeCapabilityObservation> capabilities,
        GgufModelInfo? target = null, GgufModelInfo? companion = null,
        ModelIdentityV2? provenTargetIdentity = null, ModelIdentityV2? provenCompanionIdentity = null)
    {
        var normalizedMechanism = mechanism.Trim().ToLowerInvariant();
        var capabilityId = LocalModelCapabilityService.CapabilityIdForSpeculativeType(normalizedMechanism);
        var capability = capabilities.FirstOrDefault(item => item.CapabilityId == capabilityId);
        var targetPath = source.ModelPath?.Trim() ?? string.Empty;
        var companionPath = source.Speculative?.DraftModelPath?.Trim() ?? string.Empty;
        target ??= GgufMetadataReader.TryRead(targetPath);
        companion ??= GgufMetadataReader.TryRead(companionPath);
        var targetIdentity = provenTargetIdentity ?? RuntimeIdentityFactory.CreateModelIdentity(targetPath, target);
        var companionIdentity = provenCompanionIdentity ?? RuntimeIdentityFactory.CreateModelIdentity(companionPath, companion);
        var targetBytes = SafeLength(targetPath);
        var companionBytes = SafeLength(companionPath);

        SpeculativePairInspection Result(CapabilityState state, string detail, string tokenizer = "") => new(
            normalizedMechanism, state, detail, targetIdentity, companionIdentity,
            target?.Architecture ?? string.Empty, companion?.Architecture ?? string.Empty,
            tokenizer, target?.VocabularySize, companion?.VocabularySize, targetBytes, companionBytes);

        if (capability is null || capability.State == CapabilityState.Unknown)
            return Result(CapabilityState.Unknown, "The exact runtime has not proven this speculative mechanism.");
        if (capability.State == CapabilityState.Unavailable)
            return Result(CapabilityState.Unavailable, "The exact runtime reports this speculative mechanism unavailable.");
        if (!capability.Parameters.TryGetValue("runtime_type", out var runtimeType)
            || !string.Equals(runtimeType, normalizedMechanism, StringComparison.OrdinalIgnoreCase))
            return Result(CapabilityState.Unknown,
                "The capability record does not retain this exact runtime --spec-type value.");
        if (targetPath.Length == 0 || companionPath.Length == 0)
            return Result(CapabilityState.Unknown, "Select both a target and a companion model before inspecting this recipe.");
        if (!TryFullPath(targetPath, out var fullTarget) || !TryFullPath(companionPath, out var fullCompanion)
            || string.Equals(fullTarget, fullCompanion, StringComparison.OrdinalIgnoreCase))
            return Result(CapabilityState.Unavailable, "Target and companion must be distinct valid model assets.");
        if (!ReadableRegularFile(fullTarget) || !ReadableRegularFile(fullCompanion))
            return Result(CapabilityState.Unknown, "The target and companion must be readable non-link model assets.");
        if (target is null || companion is null)
            return Result(CapabilityState.Unknown, "Both model headers must expose readable GGUF metadata.");
        if (targetIdentity.Completeness != IdentityCompleteness.Complete
            || companionIdentity.Completeness != IdentityCompleteness.Complete)
            return Result(CapabilityState.Unknown,
                "Both model assets require a verified hash or trusted manifest identity before this pair can run.");
        if (target.VocabularySize is null || companion.VocabularySize is null)
            return Result(CapabilityState.Unknown, "Both model headers must expose vocabulary size evidence.");
        if (target.VocabularySize != companion.VocabularySize)
            return Result(CapabilityState.Unknown,
                "Vocabulary sizes differ and Hermaeus cannot prove a reduced-vocabulary mapping from bounded GGUF metadata.");

        var tokenizer = target.TokenizerIdentity;
        if (tokenizer.Length == 0 || companion.TokenizerIdentity.Length == 0)
            return Result(CapabilityState.Unknown, "Both model headers must expose a tokenizer model and pre-tokenizer identity.");
        if (!string.Equals(tokenizer, companion.TokenizerIdentity, StringComparison.Ordinal))
            return Result(CapabilityState.Unavailable, "Target and companion tokenizer identities do not match.");

        if (normalizedMechanism == "draft-eagle3")
        {
            if (!HasExactTargetBinding(target, companion))
                return Result(CapabilityState.Unknown,
                    "The EAGLE-3 companion does not expose an exact base-model name or repository binding to this target.", tokenizer);
        }
        else if (!string.Equals(target.Architecture, companion.Architecture, StringComparison.OrdinalIgnoreCase))
        {
            return Result(CapabilityState.Unavailable, "Target and companion model families do not match.", tokenizer);
        }

        return Result(CapabilityState.Available,
            "Runtime mechanism, distinct assets, GGUF identities, vocabulary, tokenizer, and target binding are proven for this pair.", tokenizer);
    }

    private static bool HasExactTargetBinding(GgufModelInfo target, GgufModelInfo companion)
    {
        var targetValues = new[] { target.Name, target.RepositoryUrl, target.BaseModelName, target.BaseModelRepositoryUrl }
            .Select(NormalizeBinding).Where(value => value.Length > 0).ToHashSet(StringComparer.Ordinal);
        var companionValues = new[] { companion.BaseModelName, companion.BaseModelRepositoryUrl }
            .Select(NormalizeBinding).Where(value => value.Length > 0);
        return companionValues.Any(targetValues.Contains);
    }

    private static string NormalizeBinding(string value) =>
        value.Trim().TrimEnd('/').Replace("https://huggingface.co/", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static bool TryFullPath(string path, out string fullPath)
    {
        try { fullPath = Path.GetFullPath(path); return true; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { fullPath = string.Empty; return false; }
    }

    private static bool ReadableRegularFile(string path)
    {
        try { return File.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static long SafeLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
}
