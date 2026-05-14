using Xunit;

namespace Aether.Tests;

public sealed class XunitHarnessTests
{
    public sealed record TestCase(string Name, Func<Task> Run)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<object[]> Cases =>
    [
        [new TestCase("data root migration previews moveable files", BackupMigrationTests.DataRootMigrationPreview)],
        [new TestCase("data root migration refuses conflicts", BackupMigrationTests.DataRootMigrationRefusesConflicts)],
        [new TestCase("data root migration moves db files and leaves no junk", BackupMigrationTests.DataRootMigrationMovesFiles)],
        [new TestCase("backup excludes secrets and refuses overwrite restore", BackupMigrationTests.BackupExcludesSecretsAndRefusesOverwrite)],
        [new TestCase("redaction hides common secrets and home path", ServiceTests.RedactionHidesSecrets)],
        [new TestCase("benchmark db creates starter suites and records runs", ServiceTests.BenchmarkDbCreatesAndRecordsRuns)],
        [new TestCase("benchmark scoring and ranking are deterministic", ServiceTests.BenchmarkScoringAndRanking)],
        [new TestCase("system info returns safe fallback values", ServiceTests.SystemInfoSafeFallback)],
        [new TestCase("local ai assets detect and apply paths", ServiceTests.LocalAiAssetsDetectAndApplyPaths)],
        [new TestCase("local AI setup detects Aether folder layout", ServiceTests.LocalAiSetupDetectsFolderLayout)],
        [new TestCase("local AI setup script handling is approval gated", ServiceTests.LocalAiSetupScriptHandlingIsApprovalGated)],
        [new TestCase("local AI setup command previews stay shell-free", ServiceTests.LocalAiSetupCommandPreviewsStayShellFree)],
        [new TestCase("local AI setup surfaces Kokoro onboarding", ServiceTests.LocalAiSetupSurfcesKokoroOnboarding)],
        [new TestCase("model download resumes with range request", ServiceTests.ModelDownloadResumesWithRangeRequest)],
        [new TestCase("llama-server release data covers supported platforms", ServiceTests.LlamaServerReleaseDataCoversSupportedPlatforms)],
        [new TestCase("XTTS API template has required endpoints", ServiceTests.XttsApiTemplateHasRequiredEndpoints)],
        [new TestCase("trust scan classifies inside AI root as low risk", ServiceTests.TrustScanInsideAiRootIsLowRisk)],
        [new TestCase("trust scan warns outside AI root but allows", ServiceTests.TrustScanOutsideAiRootWarns)],
        [new TestCase("trust scan reports missing executable", ServiceTests.TrustScanReportsMissingExecutable)],
        [new TestCase("trust scan keeps unset AI root neutral", ServiceTests.TrustScanUnsetAiRootIsNeutral)],
        [new TestCase("trust scan detects network-facing extra args", ServiceTests.TrustScanDetectsNetworkExtraArgs)],
        [new TestCase("source strings avoid long dashes", ServiceTests.SourceStringsAvoidLongDashes)],
        [new TestCase("secret store falls back without plaintext", ServiceTests.SecretStoreFallbackWithoutPlaintext)],
        [new TestCase("RAG web ingest strips HTML and stores chunks", RagTests.RagWebIngestStripsHtmlAndStoresChunks)],
        [new TestCase("RAG digital PDF text extracts", RagTests.RagDigitalPdfTextExtracts)],
        [new TestCase("RAG directory ingest includes PDFs", RagTests.RagDirectoryIngestIncludesPdfs)],
        [new TestCase("RAG directory dry run reports without persisting", RagTests.RagDirectoryDryRunReportsWithoutPersisting)],
        [new TestCase("RAG directory skip unchanged avoids duplicate chunks", RagTests.RagDirectorySkipUnchangedAvoidsDuplicateChunks)],
        [new TestCase("RAG empty PDF warns and continues", RagTests.RagEmptyPdfWarnsAndContinues)],
        [new TestCase("RAG ingest cancellation during embedding stops gracefully", RagTests.RagIngestCancellationDuringEmbedding)],
        [new TestCase("RAG ingest cancellation during storage stops gracefully", RagTests.RagIngestCancellationDuringStorage)],
        [new TestCase("voice provider capability gating prevents unsupported providers", TtsTests.VoiceProviderCapabilityGating)],
        [new TestCase("voice provider legacy requires local and TTS", TtsTests.VoiceProviderLegacyRequiresLocalAndTts)],
        [new TestCase("agent task state serializes schema fields", AgentTests.AgentTaskStateSerializesSchemaFields)],
        [new TestCase("agent review queue reflects approval history", AgentTests.AgentReviewQueueReflectsApprovalHistory)],
        [new TestCase("agent workspace memory persists notes per workspace", AgentTests.AgentWorkspaceMemoryPersistsNotesPerWorkspace)],
        [new TestCase("agent workspace tools enforce path safety", AgentTests.AgentWorkspaceToolsEnforcePathSafety)],
        [new TestCase("agent context pack stays bounded", AgentTests.AgentContextPackStaysBounded)],
        [new TestCase("agent tool policy gates risky actions", AgentTests.AgentToolPolicyGatesRiskyActions)],
        [new TestCase("agent loop writes state log and trace", AgentTests.AgentLoopWritesStateLogAndTrace)],
        [new TestCase("runtime profile normalization and unsafe host validation", ServiceTests.RuntimeProfileValidation)],
        [new TestCase("runtime profile defaults are deduplicated", ServiceTests.RuntimeProfilesAreDeduplicated)],
        [new TestCase("settings save migrates OpenAI key to secret reference", ServiceTests.SettingsSaveMigratesOpenAiKey)],
        [new TestCase("settings save preserves existing secret reference", ServiceTests.SettingsSavePreservesExistingSecretReference)],
        [new TestCase("settings save persists global hotkey preference", ServiceTests.SettingsSavePersistsGlobalHotkeyPreference)],
        [new TestCase("server process arguments stay shell-free and ordered", ServiceTests.ServerProcessArgumentsAreSafe)]
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Runs_All_Cases(TestCase testCase)
    {
        await testCase.Run();
    }
}
