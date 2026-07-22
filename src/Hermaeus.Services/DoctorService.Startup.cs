using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Storage;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Voice;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hermaeus.Services;

public sealed partial class DoctorService
{
    /// <summary>
    /// Reports on the previous session's exit, using
    /// <see cref="AppLifecycleJournalService.PreviousSession"/> captured once
    /// at this session's startup (docs/review/03-next-level-roadmap.md
    /// Phase 4). A native fault (the kind the 0.9.38-0.9.40 Kokoro ONNX crash
    /// was) bypasses managed exception handling entirely and leaves no other
    /// trace; naming the last recorded operation turns "the app just
    /// vanished" into an actionable starting point.
    /// </summary>
    private DoctorCheck CheckCleanShutdown()
    {
        var previous = _lifecycleJournal?.PreviousSession;
        if (previous is null)
        {
            return BuildCheck(
                "clean-shutdown",
                "Previous session exited cleanly",
                DoctorCheckStatus.Ready,
                "No previous session recorded",
                "This looks like the first run, or the lifecycle journal is unavailable.",
                string.Empty,
                false,
                string.Empty,
                "Startup");
        }

        if (previous.CleanExit)
        {
            return BuildCheck(
                "clean-shutdown",
                "Previous session exited cleanly",
                DoctorCheckStatus.Ready,
                "Previous session exited cleanly",
                $"Last started {previous.StartedAtUtc:u}.",
                string.Empty,
                false,
                string.Empty,
                "Startup");
        }

        var crashNote = string.Empty;
        if (_runtimeLogs is not null)
        {
            try
            {
                var dir = _runtimeLogs.GetLogDirectory();
                var entry = CrashLogReader.FindNewestEntry(
                    [Path.Combine(dir, "hermaeus_unhandled.log"), Path.Combine(dir, "hermaeus_unobserved.log")],
                    previous.StartedAtUtc);
                if (entry is not null)
                    crashNote = $" The exception that likely caused it: {entry.FirstLine} (logged {entry.TimestampUtc:u}). Full details in {dir}.";
            }
            catch
            {
                // Best-effort enrichment only; the plain unclean-shutdown warning still stands.
            }
        }

        var detail = IsNeutralBreadcrumb(previous.LastOperation)
            ? "No risky operation was in progress when it stopped." + (string.IsNullOrEmpty(crashNote) ? $" If this repeats, check the crash logs in {_runtimeLogs?.GetLogDirectory() ?? "{DataRoot}/logs"}." : crashNote)
            : $"The last recorded operation was \"{previous.LastOperation}\" at {previous.LastOperationAtUtc:u}. " +
              "If Hermaeus crashed or was force-closed around then, that operation is where to start looking." + crashNote;

        return BuildCheck(
            "clean-shutdown",
            "Previous session exited cleanly",
            DoctorCheckStatus.Warning,
            "Hermaeus did not shut down cleanly last time",
            detail,
            string.Empty,
            false,
            $"StartedAtUtc={previous.StartedAtUtc:O}; LastOperation={previous.LastOperation}; LastOperationAtUtc={previous.LastOperationAtUtc:O}",
            "Startup");
    }

    /// <summary>
    /// "running" is the general post-startup breadcrumb; a "... session
    /// loaded" breadcrumb means the risky load already finished
    /// successfully. Neither names an operation that was actually in
    /// flight when a later crash happened, so blaming either would mislead
    /// (r19 1.5 - this was the owner's literal complaint: every unclean
    /// shutdown named the Kokoro startup probe even hours later).
    /// </summary>
    private static bool IsNeutralBreadcrumb(string operation) =>
        operation == "running" || operation.EndsWith("session loaded", StringComparison.Ordinal);
}
