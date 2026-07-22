namespace Hermaeus.Services;

public sealed record ModelUpdateApplyResult(bool Success, string? Error);

/// <summary>
/// The atomic-swap half of applying a Hugging Face model update (r13 03-hugging-face.md 3.3).
/// The caller is responsible for downloading to a <c>.update.tmp</c> path and verifying its
/// hash before ever calling <see cref="Swap"/> - this type only owns the crash-safe rename
/// sequence: move the current file to <c>.previous</c>, move the verified replacement into
/// place, then delete <c>.previous</c> only once both moves succeeded. No user data is
/// destroyed on any failure path: a mid-sequence failure restores the original from
/// <c>.previous</c>, and a leftover <c>.previous</c> from a prior interrupted update is
/// refused rather than silently overwritten.
/// </summary>
public static class ModelUpdateApplier
{
    public static ModelUpdateApplyResult Swap(string currentPath, string verifiedReplacementPath)
    {
        var previousPath = currentPath + ".previous";
        try
        {
            if (File.Exists(previousPath))
                return new ModelUpdateApplyResult(false,
                    $"A leftover .previous backup already exists at {previousPath} from an earlier interrupted update; remove or restore it manually before trying again.");

            File.Move(currentPath, previousPath);
            try
            {
                File.Move(verifiedReplacementPath, currentPath);
            }
            catch (Exception moveEx)
            {
                // Roll back: restore the original so the swap never happened from the user's
                // perspective, and surface the underlying failure.
                File.Move(previousPath, currentPath);
                return new ModelUpdateApplyResult(false, moveEx.Message);
            }

            File.Delete(previousPath);
            return new ModelUpdateApplyResult(true, null);
        }
        catch (Exception ex)
        {
            return new ModelUpdateApplyResult(false, ex.Message);
        }
    }
}
