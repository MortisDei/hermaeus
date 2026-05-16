using Xunit;

namespace Aether.Tests;

public sealed record HarnessCase(string Name, Func<Task> Run)
{
    public override string ToString() => Name;
}

public static class HarnessCases
{
    public static IEnumerable<object[]> Backup =>
    [
        [new HarnessCase("data root migration previews moveable files", BackupMigrationTests.DataRootMigrationPreview)],
        [new HarnessCase("data root migration refuses conflicts", BackupMigrationTests.DataRootMigrationRefusesConflicts)],
        [new HarnessCase("data root migration moves db files and leaves no junk", BackupMigrationTests.DataRootMigrationMovesFiles)],
        [new HarnessCase("backup excludes secrets and refuses overwrite restore", BackupMigrationTests.BackupExcludesSecretsAndRefusesOverwrite)]
    ];

    public static IEnumerable<object[]> Services =>
    [
        [new HarnessCase("redaction hides common secrets and home path", ServiceTests.RedactionHidesSecrets)],
        [new HarnessCase("benchmark db creates starter suites and records runs", ServiceTests.BenchmarkDbCreatesAndRecordsRuns)],
        [new HarnessCase("benchmark scoring and ranking are deterministic", ServiceTests.BenchmarkScoringAndRanking)],
        [new HarnessCase("system info returns safe fallback values", ServiceTests.SystemInfoSafeFallback)],
        [new HarnessCase("local ai assets detect and apply paths", ServiceTests.LocalAiAssetsDetectAndApplyPaths)],
        [new HarnessCase("local AI setup detects Aether folder layout", ServiceTests.LocalAiSetupDetectsFolderLayout)],
        [new HarnessCase("local AI setup script handling is approval gated", ServiceTests.LocalAiSetupScriptHandlingIsApprovalGated)],
        [new HarnessCase("local AI setup command previews stay shell-free", ServiceTests.LocalAiSetupCommandPreviewsStayShellFree)],
        [new HarnessCase("local AI setup surfaces Kokoro onboarding", ServiceTests.LocalAiSetupSurfcesKokoroOnboarding)],
        [new HarnessCase("model download resumes with range request", ServiceTests.ModelDownloadResumesWithRangeRequest)],
        [new HarnessCase("llama-server release data covers supported platforms", ServiceTests.LlamaServerReleaseDataCoversSupportedPlatforms)],
        [new HarnessCase("XTTS API template has required endpoints", ServiceTests.XttsApiTemplateHasRequiredEndpoints)],
        [new HarnessCase("trust scan classifies inside AI root as low risk", ServiceTests.TrustScanInsideAiRootIsLowRisk)],
        [new HarnessCase("trust scan warns outside AI root but allows", ServiceTests.TrustScanOutsideAiRootWarns)],
        [new HarnessCase("trust scan reports missing executable", ServiceTests.TrustScanReportsMissingExecutable)],
        [new HarnessCase("trust scan keeps unset AI root neutral", ServiceTests.TrustScanUnsetAiRootIsNeutral)],
        [new HarnessCase("trust scan detects network-facing extra args", ServiceTests.TrustScanDetectsNetworkExtraArgs)],
        [new HarnessCase("source strings avoid long dashes", ServiceTests.SourceStringsAvoidLongDashes)],
        [new HarnessCase("secret store falls back without plaintext", ServiceTests.SecretStoreFallbackWithoutPlaintext)],
        [new HarnessCase("runtime profile normalization and unsafe host validation", ServiceTests.RuntimeProfileValidation)],
        [new HarnessCase("runtime profile defaults are deduplicated", ServiceTests.RuntimeProfilesAreDeduplicated)],
        [new HarnessCase("settings save migrates OpenAI key to secret reference", ServiceTests.SettingsSaveMigratesOpenAiKey)],
        [new HarnessCase("settings save preserves existing secret reference", ServiceTests.SettingsSavePreservesExistingSecretReference)],
        [new HarnessCase("settings save persists global hotkey preference", ServiceTests.SettingsSavePersistsGlobalHotkeyPreference)],
        [new HarnessCase("server process arguments stay shell-free and ordered", ServiceTests.ServerProcessArgumentsAreSafe)],
        [new HarnessCase("conversation auto-summary stores memories when important", ServiceTests.ConversationAutoSummaryStoresMemoriesWhenImportant)],
        [new HarnessCase("memory store CRUD and search works", ServiceTests.MemoryStoreCrudAndSearchWorks)],
        [new HarnessCase("memory extraction parses and cleans markers", ServiceTests.MemoryExtractionParsesAndCleansMarkers)],
        [new HarnessCase("memory injection respects token budget and priority", ServiceTests.MemoryInjectionRespectsTokenBudgetAndPriority)],
        [new HarnessCase("agent workspace draft patch formats preview", ServiceTests.AgentWorkspaceDraftPatch)],
        [new HarnessCase("agent workspace apply draft patch writes file", ServiceTests.AgentWorkspaceApplyDraftPatchWritesFile)],
        [new HarnessCase("agent draft patch queue and approval round-trips", ServiceTests.AgentDraftPatchQueueAndApproval)]
    ];

    public static IEnumerable<object[]> Rag =>
    [
        [new HarnessCase("RAG web ingest strips HTML and stores chunks", RagTests.RagWebIngestStripsHtmlAndStoresChunks)],
        [new HarnessCase("RAG digital PDF text extracts", RagTests.RagDigitalPdfTextExtracts)],
        [new HarnessCase("RAG directory ingest includes PDFs", RagTests.RagDirectoryIngestIncludesPdfs)],
        [new HarnessCase("RAG directory dry run reports without persisting", RagTests.RagDirectoryDryRunReportsWithoutPersisting)],
        [new HarnessCase("RAG directory skip unchanged avoids duplicate chunks", RagTests.RagDirectorySkipUnchangedAvoidsDuplicateChunks)],
        [new HarnessCase("RAG empty PDF warns and continues", RagTests.RagEmptyPdfWarnsAndContinues)],
        [new HarnessCase("RAG ingest cancellation during embedding stops gracefully", RagTests.RagIngestCancellationDuringEmbedding)],
        [new HarnessCase("RAG ingest cancellation during storage stops gracefully", RagTests.RagIngestCancellationDuringStorage)]
    ];

    public static IEnumerable<object[]> Tts =>
    [
        [new HarnessCase("voice provider capability gating prevents unsupported providers", TtsTests.VoiceProviderCapabilityGating)],
        [new HarnessCase("voice provider XTTS v2 requires local and TTS", TtsTests.VoiceProviderXttsV2RequiresLocalAndTts)]
    ];

    public static IEnumerable<object[]> Agent =>
    [
        [new HarnessCase("agent task state serializes schema fields", AgentTests.AgentTaskStateSerializesSchemaFields)],
        [new HarnessCase("agent review queue reflects approval history", AgentTests.AgentReviewQueueReflectsApprovalHistory)],
        [new HarnessCase("agent workspace memory persists notes per workspace", AgentTests.AgentWorkspaceMemoryPersistsNotesPerWorkspace)],
        [new HarnessCase("agent workspace tools enforce path safety", AgentTests.AgentWorkspaceToolsEnforcePathSafety)],
        [new HarnessCase("agent task state persists queued draft patches", AgentTests.AgentTaskStatePersistsQueuedDraftPatches)],
        [new HarnessCase("agent context pack stays bounded", AgentTests.AgentContextPackStaysBounded)],
        [new HarnessCase("agent tool policy gates risky actions", AgentTests.AgentToolPolicyGatesRiskyActions)],
        [new HarnessCase("agent loop writes state log and trace", AgentTests.AgentLoopWritesStateLogAndTrace)]
    ];
}

public sealed class BackupHarnessTests
{
    [Theory]
    [MemberData(nameof(HarnessCases.Backup), MemberType = typeof(HarnessCases))]
    public async Task Runs_Backup_Cases(HarnessCase testCase)
    {
        await testCase.Run();
    }
}

public sealed class ServiceHarnessTests
{
    [Theory]
    [MemberData(nameof(HarnessCases.Services), MemberType = typeof(HarnessCases))]
    public async Task Runs_Service_Cases(HarnessCase testCase)
    {
        await testCase.Run();
    }
}

public sealed class RagHarnessTests
{
    [Theory]
    [MemberData(nameof(HarnessCases.Rag), MemberType = typeof(HarnessCases))]
    public async Task Runs_Rag_Cases(HarnessCase testCase)
    {
        await testCase.Run();
    }
}

public sealed class TtsHarnessTests
{
    [Theory]
    [MemberData(nameof(HarnessCases.Tts), MemberType = typeof(HarnessCases))]
    public async Task Runs_Tts_Cases(HarnessCase testCase)
    {
        await testCase.Run();
    }
}

public sealed class AgentHarnessTests
{
    [Theory]
    [MemberData(nameof(HarnessCases.Agent), MemberType = typeof(HarnessCases))]
    public async Task Runs_Agent_Cases(HarnessCase testCase)
    {
        await testCase.Run();
    }
}
