using System;
using System.Threading.Tasks;
using Aether.Tests;

var tests = new (string Name, Func<Task> Run)[]
{
    ("data root migration previews moveable files", BackupMigrationTests.DataRootMigrationPreview),
    ("data root migration refuses conflicts", BackupMigrationTests.DataRootMigrationRefusesConflicts),
    ("data root migration moves db files and leaves no junk", BackupMigrationTests.DataRootMigrationMovesFiles),
    ("backup excludes secrets and refuses overwrite restore", BackupMigrationTests.BackupExcludesSecretsAndRefusesOverwrite),
    ("redaction hides common secrets and home path", ServiceTests.RedactionHidesSecrets),
    ("benchmark db creates starter suites and records runs", ServiceTests.BenchmarkDbCreatesAndRecordsRuns),
    ("benchmark scoring and ranking are deterministic", ServiceTests.BenchmarkScoringAndRanking),
    ("system info returns safe fallback values", ServiceTests.SystemInfoSafeFallback),
    ("local ai assets detect and apply paths", ServiceTests.LocalAiAssetsDetectAndApplyPaths),
    ("local AI setup detects Aether folder layout", ServiceTests.LocalAiSetupDetectsFolderLayout),
    ("local AI setup script handling is approval gated", ServiceTests.LocalAiSetupScriptHandlingIsApprovalGated),
    ("local AI setup command previews stay shell-free", ServiceTests.LocalAiSetupCommandPreviewsStayShellFree),
    ("XTTS API template has required endpoints", ServiceTests.XttsApiTemplateHasRequiredEndpoints),
    ("trust scan classifies inside AI root as low risk", ServiceTests.TrustScanInsideAiRootIsLowRisk),
    ("trust scan warns outside AI root but allows", ServiceTests.TrustScanOutsideAiRootWarns),
    ("trust scan reports missing executable", ServiceTests.TrustScanReportsMissingExecutable),
    ("trust scan keeps unset AI root neutral", ServiceTests.TrustScanUnsetAiRootIsNeutral),
    ("trust scan detects network-facing extra args", ServiceTests.TrustScanDetectsNetworkExtraArgs),
    ("source strings avoid long dashes", ServiceTests.SourceStringsAvoidLongDashes),
    ("secret store falls back without plaintext", ServiceTests.SecretStoreFallbackWithoutPlaintext),
    ("RAG web ingest strips HTML and stores chunks", RagTests.RagWebIngestStripsHtmlAndStoresChunks),
    ("RAG digital PDF text extracts", RagTests.RagDigitalPdfTextExtracts),
    ("RAG directory ingest includes PDFs", RagTests.RagDirectoryIngestIncludesPdfs),
    ("RAG directory dry run reports without persisting", RagTests.RagDirectoryDryRunReportsWithoutPersisting),
    ("RAG directory skip unchanged avoids duplicate chunks", RagTests.RagDirectorySkipUnchangedAvoidsDuplicateChunks),
    ("RAG empty PDF warns and continues", RagTests.RagEmptyPdfWarnsAndContinues),
    ("RAG ingest cancellation during embedding stops gracefully", RagTests.RagIngestCancellationDuringEmbedding),
    ("RAG ingest cancellation during storage stops gracefully", RagTests.RagIngestCancellationDuringStorage),
    ("voice provider capability gating prevents unsupported providers", TtsTests.VoiceProviderCapabilityGating),
    ("voice provider legacy requires local and TTS", TtsTests.VoiceProviderLegacyRequiresLocalAndTts),
    ("agent task state serializes schema fields", AgentTests.AgentTaskStateSerializesSchemaFields),
    ("agent review queue reflects approval history", AgentTests.AgentReviewQueueReflectsApprovalHistory),
    ("agent workspace memory persists notes per workspace", AgentTests.AgentWorkspaceMemoryPersistsNotesPerWorkspace),
    ("agent workspace tools enforce path safety", AgentTests.AgentWorkspaceToolsEnforcePathSafety),
    ("agent context pack stays bounded", AgentTests.AgentContextPackStaysBounded),
    ("agent tool policy gates risky actions", AgentTests.AgentToolPolicyGatesRiskyActions),
    ("agent loop writes state log and trace", AgentTests.AgentLoopWritesStateLogAndTrace),
    ("runtime profile normalization and unsafe host validation", ServiceTests.RuntimeProfileValidation),
    ("runtime profile defaults are deduplicated", ServiceTests.RuntimeProfilesAreDeduplicated),
    ("settings save migrates OpenAI key to secret reference", ServiceTests.SettingsSaveMigratesOpenAiKey),
    ("settings save preserves existing secret reference", ServiceTests.SettingsSavePreservesExistingSecretReference),
    ("settings save persists global hotkey preference", ServiceTests.SettingsSavePersistsGlobalHotkeyPreference),
    ("server process arguments stay shell-free and ordered", ServiceTests.ServerProcessArgumentsAreSafe)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failed > 0)
    return failed;

Console.WriteLine($"All {tests.Length} Aether tests passed.");
return 0;