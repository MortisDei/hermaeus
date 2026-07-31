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
