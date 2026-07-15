using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag.Embeddings;
using Aether.Rag.Storage;
using Aether.Rag.Retrieval;
using Aether.Voice;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Aether.Services;

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

        return BuildCheck(
            "clean-shutdown",
            "Previous session exited cleanly",
            DoctorCheckStatus.Warning,
            "Aether did not shut down cleanly last time",
            $"The last recorded operation was \"{previous.LastOperation}\" at {previous.LastOperationAtUtc:u}. " +
            "If Aether crashed or was force-closed around then, that operation is where to start looking.",
            string.Empty,
            false,
            $"StartedAtUtc={previous.StartedAtUtc:O}; LastOperation={previous.LastOperation}; LastOperationAtUtc={previous.LastOperationAtUtc:O}",
            "Startup");
    }
}
