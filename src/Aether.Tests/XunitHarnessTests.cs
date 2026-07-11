using Xunit;

// Harness cases share temp data roots and SQLite pools; run sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

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
        [new HarnessCase("settings save without previous root skips migration", BackupMigrationTests.SaveWithoutPreviousDataRootDoesNotAttemptMigration)],
        [new HarnessCase("data root migration moves db files and leaves no junk", BackupMigrationTests.DataRootMigrationMovesFiles)],
        [new HarnessCase("backup excludes secrets and refuses overwrite restore", BackupMigrationTests.BackupExcludesSecretsAndRefusesOverwrite)],
        [new HarnessCase("backup restore rejects unsafe path prefixes", BackupMigrationTests.BackupRestoreRejectsUnsafePathPrefix)],
        [new HarnessCase("backup restore rejects case-variant sibling paths", BackupMigrationTests.BackupRestoreRejectsCaseVariantSiblingOnCaseSensitiveFileSystems)]
    ];

    public static IEnumerable<object[]> Services =>
    [
        [new HarnessCase("redaction hides common secrets and home path", ServiceTests.RedactionHidesSecrets)],
        [new HarnessCase("benchmark db creates starter suites and records runs", ServiceTests.BenchmarkDbCreatesAndRecordsRuns)],
        [new HarnessCase("benchmark starter suites include expanded deterministic set", ServiceTests.BenchmarkStarterSuitesIncludeExpandedDeterministicSet)],
        [new HarnessCase("benchmark single iteration exports cold run mode", ServiceTests.BenchmarkSingleIterationRunExportsColdRunMode)],
        [new HarnessCase("benchmark run history can be cleared", ServiceTests.BenchmarkRunHistoryCanBeCleared)],
        [new HarnessCase("benchmark scoring and ranking are deterministic", ServiceTests.BenchmarkScoringAndRanking)],
        [new HarnessCase("system info returns safe fallback values", ServiceTests.SystemInfoSafeFallback)],
        [new HarnessCase("privacy audit reports remote providers and exposed servers", ServiceTests.PrivacyAuditReportsRemoteAndNetworkExposure)],
        [new HarnessCase("privacy audit flags a remote voice provider even with no chat provider enabled", ServiceTests.PrivacyAuditFlagsRemoteVoiceProviderWithNoChatProviderEnabled)],
        [new HarnessCase("privacy audit shows per-app local API activity", ServiceTests.PrivacyAuditShowsPerAppLocalApiActivity)],
        [new HarnessCase("local ai assets detect and apply paths", ServiceTests.LocalAiAssetsDetectAndApplyPaths)],
        [new HarnessCase("local ai assets prefer existing Models directory with GGUFs", ServiceTests.LocalAiAssetsPreferExistingModelsDirectoryWithGgufs)],
        [new HarnessCase("local ai assets list discovered GGUF models", ServiceTests.LocalAiAssetsListsDiscoveredGgufModels)],
        [new HarnessCase("local ai assets list discovered embedding models", ServiceTests.LocalAiAssetsListsDiscoveredEmbeddingModels)],
        [new HarnessCase("local ai assets list discovered reranker directories", ServiceTests.LocalAiAssetsListsDiscoveredRerankerDirectories)],
        [new HarnessCase("RAG settings preserve configured embedding model option", ServiceTests.RagSettingsPreservesConfiguredEmbeddingModelOption)],
        [new HarnessCase("RAG settings discover and select installed reranker", ServiceTests.RagSettingsDiscoversAndSelectsInstalledReranker)],
        [new HarnessCase("app lifecycle journal tracks clean and unclean exits", ServiceTests.AppLifecycleJournalTracksCleanAndUncleanExits)],
        [new HarnessCase("Doctor warns when previous session did not exit cleanly", ServiceTests.DoctorWarnsWhenPreviousSessionDidNotExitCleanly)],
        [new HarnessCase("Doctor does not treat chat GGUF as embedding model", ServiceTests.DoctorDoesNotTreatChatGgufAsEmbeddingModel)],
        [new HarnessCase("Doctor warns for untuned local GGUF models", ServiceTests.DoctorWarnsForUntunedLocalGgufModels)],
        [new HarnessCase("Doctor startup scan raises problem toast", ServiceTests.DoctorStartupScanRaisesProblemToast)],
        [new HarnessCase("local AI setup detects Aether folder layout", ServiceTests.LocalAiSetupDetectsFolderLayout)],
        [new HarnessCase("local AI setup script handling is approval gated", ServiceTests.LocalAiSetupScriptHandlingIsApprovalGated)],
        [new HarnessCase("local AI setup command previews stay shell-free", ServiceTests.LocalAiSetupCommandPreviewsStayShellFree)],
        [new HarnessCase("local AI setup does not ship placeholder hashes", ServiceTests.LocalAiSetupDoesNotShipPlaceholderHashes)],
        [new HarnessCase("local AI setup surfaces Kokoro onboarding", ServiceTests.LocalAiSetupSurfcesKokoroOnboarding)],
        [new HarnessCase("model download resumes with range request", ServiceTests.ModelDownloadResumesWithRangeRequest)],
        [new HarnessCase("Doctor embedding install verifies hash and configures server", ServiceTests.DoctorEmbeddingInstallVerifiesHashAndConfiguresServer)],
        [new HarnessCase("Doctor embedding install rejects hash mismatch", ServiceTests.DoctorEmbeddingInstallRejectsHashMismatch)],
        [new HarnessCase("Doctor embedding install migrates root embedding model", ServiceTests.DoctorEmbeddingInstallMigratesRootEmbeddingModel)],
        [new HarnessCase("llama-server release data covers supported platforms", ServiceTests.LlamaServerReleaseDataCoversSupportedPlatforms)],
        [new HarnessCase("llama-server latest asset selection finds current platform", ServiceTests.LlamaServerLatestAssetSelectionFindsCurrentPlatform)],
        [new HarnessCase("OpenAI voice resolves secret references", ServiceTests.OpenAiVoiceResolvesSecretReferences)],
        [new HarnessCase("llama-server PATH lookup skips empty segments", ServiceTests.LlamaServerPathLookupSkipsEmptyPathSegments)],
        [new HarnessCase("XTTS API template has required endpoints", ServiceTests.XttsApiTemplateHasRequiredEndpoints)],
        [new HarnessCase("XTTS API template escapes configured paths", ServiceTests.XttsApiTemplateEscapesConfiguredPaths)],
        [new HarnessCase("embedding client surfaces actionable 501 hints", ServiceTests.EmbeddingClientSurfacesActionableHintWhenEndpointIsNotImplemented)],
        [new HarnessCase("trust scan classifies inside AI root as low risk", ServiceTests.TrustScanInsideAiRootIsLowRisk)],
        [new HarnessCase("trust scan warns outside AI root but allows", ServiceTests.TrustScanOutsideAiRootWarns)],
        [new HarnessCase("trust scan reports missing executable", ServiceTests.TrustScanReportsMissingExecutable)],
        [new HarnessCase("trust scan keeps unset AI root neutral", ServiceTests.TrustScanUnsetAiRootIsNeutral)],
        [new HarnessCase("trust scan detects network-facing extra args", ServiceTests.TrustScanDetectsNetworkExtraArgs)],
        [new HarnessCase("source strings avoid long dashes", ServiceTests.SourceStringsAvoidLongDashes)],
        [new HarnessCase("secret store falls back without plaintext", ServiceTests.SecretStoreFallbackWithoutPlaintext)],
        [new HarnessCase("secret store key file is written atomically with restricted permissions", ServiceTests.SecretStoreKeyFileIsWrittenAtomicallyWithRestrictedPermissions)],
        [new HarnessCase("secret store logs a warning when a stored secret cannot be decrypted", ServiceTests.SecretStoreLogsWarningWhenStoredSecretCannotBeDecrypted)],
        [new HarnessCase("runtime profile normalization and unsafe host validation", ServiceTests.RuntimeProfileValidation)],
        [new HarnessCase("runtime profile defaults are deduplicated", ServiceTests.RuntimeProfilesAreDeduplicated)],
        [new HarnessCase("settings save migrates OpenAI key to secret reference", ServiceTests.SettingsSaveMigratesOpenAiKey)],
        [new HarnessCase("settings load migrates legacy shared local API token to named entry", ServiceTests.SettingsLoadMigratesLegacySharedLocalApiTokenToNamedEntry)],
        [new HarnessCase("settings load backs up unreadable JSON", ServiceTests.SettingsLoadBacksUpUnreadableJson)],
        [new HarnessCase("settings save prunes per-conversation memory overrides", ServiceTests.SettingsSavePrunesPerConversationMemoryOverrides)],
        [new HarnessCase("settings save deduplicates default managed servers", ServiceTests.SettingsSaveDeduplicatesDefaultManagedServers)],
        [new HarnessCase("settings child view models apply to settings", ServiceTests.SettingsChildViewModelsApplyToSettings)],
        [new HarnessCase("draft patch preview decision completes", ServiceTests.DraftPatchPreviewDecisionCompletes)],
        [new HarnessCase("settings save preserves existing secret reference", ServiceTests.SettingsSavePreservesExistingSecretReference)],
        [new HarnessCase("settings save persists global hotkey preference", ServiceTests.SettingsSavePersistsGlobalHotkeyPreference)],
        [new HarnessCase("server process arguments stay shell-free and ordered", ServiceTests.ServerProcessArgumentsAreSafe)],
        [new HarnessCase("server process keeps explicit embedding pooling", ServiceTests.ServerProcessArgumentsKeepExplicitPoolingChoice)],
        [new HarnessCase("server auto-tune plans descending GPU candidates", ServiceTests.ServerAutoTunePlansDescendingGpuCandidates)],
        [new HarnessCase("embedding client surfaces pooling compatibility hints", ServiceTests.EmbeddingClientSurfacesPoolingHintWhenServerRejectsNonePooling)],
        [new HarnessCase("conversation auto-summary stores memories when important", ServiceTests.ConversationAutoSummaryStoresMemoriesWhenImportant)],
        [new HarnessCase("memory store CRUD and search works", ServiceTests.MemoryStoreCrudAndSearchWorks)],
        [new HarnessCase("memory store round trips explicit source and backfills legacy rows", ServiceTests.MemoryStoreRoundTripsExplicitSourceAndBackfillsLegacyRows)],
        [new HarnessCase("memory extraction parses and cleans markers", ServiceTests.MemoryExtractionParsesAndCleansMarkers)],
        [new HarnessCase("memory extraction parses structured JSON with model-supplied metadata", ServiceTests.MemoryExtractionParsesStructuredJsonWithModelSuppliedMetadata)],
        [new HarnessCase("memory extraction structured parsing falls back gracefully on garbage", ServiceTests.MemoryExtractionStructuredParsingFallsBackGracefullyOnGarbage)],
        [new HarnessCase("memory injection respects token budget and priority", ServiceTests.MemoryInjectionRespectsTokenBudgetAndPriority)],
        [new HarnessCase("memory injection uses full budget", ServiceTests.MemoryInjectionUsesFullBudget)],
        [new HarnessCase("memory injection prefers search relevance over raw importance", ServiceTests.MemoryInjectionPrefersSearchRelevanceOverRawImportance)],
        [new HarnessCase("memory lifecycle decays unrecalled memories and archives below floor", ServiceTests.MemoryLifecycleDecaysUnrecalledMemoriesAndArchivesBelowFloor)],
        [new HarnessCase("conversation memory applies update and forget markers only for injected ids", ServiceTests.ConversationMemoryAppliesUpdateAndForgetMarkersOnlyForInjectedIds)],
        [new HarnessCase("XTTS API template delegates to generator", ServiceTests.XttsApiTemplateDelegatesToGenerator)],
        [new HarnessCase("extra args parser handles escaped quotes", ServiceTests.ExtraArgsParserHandlesEscapedQuotes)],
        [new HarnessCase("benchmark CSV normalizes embedded newlines", ServiceTests.BenchmarkCsvNormalizesEmbeddedNewlines)],
        [new HarnessCase("reranker hash verification rejects mismatch", ServiceTests.RerankerHashVerificationRejectsMismatch)],
        [new HarnessCase("conversation export produces markdown and json", ServiceTests.ConversationExportProducesMarkdownAndJson)],
        [new HarnessCase("agent workspace draft patch formats preview", ServiceTests.AgentWorkspaceDraftPatch)],
        [new HarnessCase("agent workspace apply draft patch writes file", ServiceTests.AgentWorkspaceApplyDraftPatchWritesFile)],
        [new HarnessCase("agent draft patch queue and approval round-trips", ServiceTests.AgentDraftPatchQueueAndApproval)],
        [new HarnessCase("inspection engine filters providers by view", ServiceTests.InspectionEngineFiltersProvidersByView)],
        [new HarnessCase("inspection engine turns a provider failure into an error check", ServiceTests.InspectionEngineReportsProviderFailureAsErrorCheck)],
        [new HarnessCase("Doctor and Trust and Privacy contribute checks to their own view", ServiceTests.DoctorTrustPrivacyContributeChecksToOwnView)],
        [new HarnessCase("local API process manager resolves packaged executable first", ServiceTests.LocalApiProcessManagerResolvesPackagedExecutableFirst)],
        [new HarnessCase("local API process manager falls back to dev build output", ServiceTests.LocalApiProcessManagerFallsBackToDevBuildOutput)],
        [new HarnessCase("local API process manager returns null when nothing is built", ServiceTests.LocalApiProcessManagerReturnsNullWhenNothingIsBuilt)]
    ];

    public static IEnumerable<object[]> Rag =>
    [
        [new HarnessCase("RAG web ingest strips HTML and stores chunks", RagTests.RagWebIngestStripsHtmlAndStoresChunks)],
        [new HarnessCase("RAG digital PDF text extracts", RagTests.RagDigitalPdfTextExtracts)],
        [new HarnessCase("RAG directory ingest includes PDFs", RagTests.RagDirectoryIngestIncludesPdfs)],
        [new HarnessCase("RAG directory ingest reports overall progress", RagTests.RagDirectoryIngestReportsOverallProgress)],
        [new HarnessCase("RAG directory dry run reports without persisting", RagTests.RagDirectoryDryRunReportsWithoutPersisting)],
        [new HarnessCase("RAG directory skip unchanged avoids duplicate chunks", RagTests.RagDirectorySkipUnchangedAvoidsDuplicateChunks)],
        [new HarnessCase("RAG empty PDF warns and continues", RagTests.RagEmptyPdfWarnsAndContinues)],
        [new HarnessCase("RAG ingest cancellation during embedding stops gracefully", RagTests.RagIngestCancellationDuringEmbedding)],
        [new HarnessCase("RAG ingest cancellation during storage stops gracefully", RagTests.RagIngestCancellationDuringStorage)],
        [new HarnessCase("RAG ingest clamps oversized embedding inputs", RagTests.RagIngestClampsOversizedEmbeddingInputs)]
    ];

    public static IEnumerable<object[]> Tts =>
    [
        [new HarnessCase("voice provider capability gating prevents unsupported providers", TtsTests.VoiceProviderCapabilityGating)],
        [new HarnessCase("voice provider XTTS v2 requires local and TTS", TtsTests.VoiceProviderXttsV2RequiresLocalAndTts)],
        [new HarnessCase("voice device options include Apple Silicon MPS", TtsTests.VoiceDeviceOptionsIncludeMps)]
    ];

    public static IEnumerable<object[]> Agent =>
    [
        [new HarnessCase("agent task state serializes schema fields", AgentTests.AgentTaskStateSerializesSchemaFields)],
        [new HarnessCase("agent task state rejects unsafe task ids", AgentTests.AgentTaskStateRejectsUnsafeTaskIds)],
        [new HarnessCase("agent review queue reflects approval history", AgentTests.AgentReviewQueueReflectsApprovalHistory)],
        [new HarnessCase("agent task state uses SQLite index for lists", AgentTests.AgentTaskStateUsesSqliteIndexForLists)],
        [new HarnessCase("agent task index reconciles JSON source of truth", AgentTests.AgentTaskIndexReconcilesJsonSourceOfTruth)],
        [new HarnessCase("agent workspace memory persists notes per workspace", AgentTests.AgentWorkspaceMemoryPersistsNotesPerWorkspace)],
        [new HarnessCase("agent workspace analysis builds profile", AgentTests.AgentWorkspaceAnalysisBuildsProfile)],
        [new HarnessCase("agent workspace tools enforce path safety", AgentTests.AgentWorkspaceToolsEnforcePathSafety)],
        [new HarnessCase("agent edit_file requires a unique match", AgentTests.AgentEditFileRequiresUniqueMatch)],
        [new HarnessCase("agent create_file refuses to overwrite existing files", AgentTests.AgentCreateFileRefusesToOverwriteExisting)],
        [new HarnessCase("agent glob_files matches patterns", AgentTests.AgentGlobFilesMatchesPatterns)],
        [new HarnessCase("agent read_file pages by line", AgentTests.AgentReadFilePagesByLine)],
        [new HarnessCase("agent search_files supports regex and context", AgentTests.AgentSearchFilesSupportsRegexAndContext)],
        [new HarnessCase("agent set_plan updates task state without approval", AgentTests.AgentSetPlanUpdatesTaskStateWithoutApproval)],
        [new HarnessCase("agent task state persists queued draft patches", AgentTests.AgentTaskStatePersistsQueuedDraftPatches)],
        [new HarnessCase("agent task state persists blocked draft patches", AgentTests.AgentTaskStatePersistsBlockedDraftPatches)],
        [new HarnessCase("agent draft patch view model shows outcome labels", AgentTests.AgentDraftPatchViewModelShowsOutcomeLabels)],
        [new HarnessCase("agent context pack stays bounded", AgentTests.AgentContextPackStaysBounded)],
        [new HarnessCase("agent context pack includes activated project instructions", AgentTests.AgentContextPackIncludesActivatedProjectInstructions)],
        [new HarnessCase("agent tool policy gates risky actions", AgentTests.AgentToolPolicyGatesRiskyActions)],
        [new HarnessCase("agent safety gate evaluate command only allows declared safe recipes", AgentTests.AgentSafetyGateEvaluateCommandOnlyAllowsDeclaredSafeRecipes)],
        [new HarnessCase("agent safety gate always requires approval for mcp tools", AgentTests.AgentSafetyGateAlwaysRequiresApprovalForMcpTools)],
        [new HarnessCase("agent tool executor runs declared command recipe", AgentTests.AgentToolExecutorRunsDeclaredCommandRecipe)],
        [new HarnessCase("run_command accepts an optional path within the workspace", AgentTests.RunCommandAcceptsAnOptionalPathWithinTheWorkspace)],
        [new HarnessCase("run_command npm run only allows scripts declared in package.json", AgentTests.RunCommandNpmRunOnlyAllowsScriptsDeclaredInPackageJson)],
        [new HarnessCase("agent safety gate declared family covers argument variants", AgentTests.AgentSafetyGateDeclaredFamilyCoversArgumentVariants)],
        [new HarnessCase("agent remembered command approval only applies to the exact command in the same task", AgentTests.AgentRememberedCommandApprovalOnlyAppliesToTheExactCommandInTheSameTask)],
        [new HarnessCase("run_command output surfaces compiler errors for the model to see", AgentTests.RunCommandOutputSurfacesCompilerErrorsForTheModelToSee)],
        [new HarnessCase("run_command recipe case sensitivity matches between gate and executor", AgentTests.RunCommandRecipeCaseSensitivityMatchesBetweenGateAndExecutor)],
        [new HarnessCase("agent tool executor inspect git diff handles large status output", AgentTests.AgentToolExecutorInspectGitDiffHandlesLargeStatusOutput)],
        [new HarnessCase("agent tool executor populates source reference for file tools", AgentTests.AgentToolExecutorPopulatesSourceReferenceForFileTools)],
        [new HarnessCase("agent loop writes state log and trace", AgentTests.AgentLoopWritesStateLogAndTrace)],
        [new HarnessCase("agent transcript feeds following step context pack", AgentTests.AgentTranscriptFeedsFollowingStepContextPack)],
        [new HarnessCase("agent RunAsync loops until final answer", AgentTests.AgentRunAsyncLoopsUntilFinalAnswer)],
        [new HarnessCase("agent RunAsync stops at max auto steps", AgentTests.AgentRunAsyncStopsAtMaxAutoSteps)],
        [new HarnessCase("agent RunAsync stops when approval is required", AgentTests.AgentRunAsyncStopsWhenApprovalIsRequired)],
        [new HarnessCase("agent consumes native tool calls without JSON text parsing", AgentTests.AgentConsumesNativeToolCallsWithoutJsonTextParsing)],
        [new HarnessCase("agent captures command failure lesson and injects it next step", AgentTests.AgentCapturesCommandFailureLessonAndInjectsItNextStep)],
        [new HarnessCase("agent captures approval rejection lesson", AgentTests.AgentCapturesApprovalRejectionLesson)],
        [new HarnessCase("agent captures stated lesson marker and strips it from user message", AgentTests.AgentCapturesStatedLessonMarkerAndStripsItFromUserMessage)],
        [new HarnessCase("workspace manifest round trips through in-repo file", AgentTests.WorkspaceManifestRoundTripsThroughInRepoFile)],
        [new HarnessCase("workspace activation prefers manifest over profile", AgentTests.WorkspaceActivationPrefersManifestOverProfile)],
        [new HarnessCase("workspace manifest requires an existing workspace root", AgentTests.WorkspaceManifestRequiresAnExistingWorkspaceRoot)],
        [new HarnessCase("agent user reply records transcript entry and resumes the task", AgentTests.AgentUserReplyRecordsTranscriptEntryAndResumesTheTask)],
        [new HarnessCase("agent user reply is refused when a tool approval is pending", AgentTests.AgentUserReplyIsRefusedWhenAToolApprovalIsPending)],
        [new HarnessCase("agent approved tool execution reaches the transcript", AgentTests.AgentApprovedToolExecutionReachesTheTranscript)],
        [new HarnessCase("agent fails after three consecutive unparseable responses", AgentTests.AgentFailsAfterThreeConsecutiveUnparseableResponses)],
        [new HarnessCase("agent consecutive step error counter resets on a valid response", AgentTests.AgentConsecutiveStepErrorCounterResetsOnAValidResponse)],
        [new HarnessCase("agent native tool call preserves prose and lists dropped calls", AgentTests.AgentNativeToolCallPreservesProseAndListsDroppedCalls)],
        [new HarnessCase("agent command lesson signature excludes outcome so contradiction can reach it", AgentTests.AgentCommandLessonSignatureExcludesOutcomeSoContradictionCanReachIt)],
        [new HarnessCase("agent approving a previously rejected tool weakens the rejection lesson", AgentTests.AgentApprovingAPreviouslyRejectedToolWeakensTheRejectionLesson)],
        [new HarnessCase("agent tool executor populates structured exit code for run_command", AgentTests.AgentToolExecutorPopulatesStructuredExitCodeForRunCommand)],
        [new HarnessCase("agent records task completion lesson only when prior failure occurred", AgentTests.AgentRecordsTaskCompletionLessonOnlyWhenPriorFailureOccurred)],
        [new HarnessCase("agent records task failure lesson with blocker when task fails", AgentTests.AgentRecordsTaskFailureLessonWithBlockerWhenTaskFails)],
        [new HarnessCase("agent confirms injected lessons on successful task completion", AgentTests.AgentConfirmsInjectedLessonsOnSuccessfulTaskCompletion)],
        [new HarnessCase("agent context builder ranks lessons by goal relevance over raw confidence", AgentTests.AgentContextBuilderRanksLessonsByGoalRelevanceOverRawConfidence)]
    ];

    public static IEnumerable<object[]> Mcp =>
    [
        [new HarnessCase("MCP client completes handshake and lists tools", McpTests.McpClientCompletesHandshakeAndListsTools)],
        [new HarnessCase("MCP client calls tool and returns text content", McpTests.McpClientCallsToolAndReturnsTextContent)],
        [new HarnessCase("MCP bridge parses namespaced tool names", McpTests.McpBridgeParsesNamespacedToolNames)],
        [new HarnessCase("MCP bridge allowlist restricts which declared tools can execute", McpTests.McpBridgeAllowlistRestrictsWhichDeclaredToolsCanExecute)],
        [new HarnessCase("MCP bridge execute rejects tools not declared by the server even if allowlisted", McpTests.McpBridgeExecuteRejectsToolsNotDeclaredByTheServerEvenIfAllowlisted)],
        [new HarnessCase("MCP client preserves argument JSON types", McpTests.McpClientPreservesArgumentJsonTypes)],
        [new HarnessCase("MCP client fails fast when server closes connection", McpTests.McpClientFailsFastWhenServerClosesConnection)],
        [new HarnessCase("MCP client drains stderr without blocking calls", McpTests.McpClientDrainsStderrWithoutBlockingCalls)]
    ];

    public static IEnumerable<object[]> LocalApi =>
    [
        [new HarnessCase("chat completion endpoint returns aggregated content", LocalApiTests.ChatCompletionEndpointReturnsAggregatedContent)],
        [new HarnessCase("chat completion streams server sent events when requested", LocalApiTests.ChatCompletionStreamsServerSentEventsWhenRequested)],
        [new HarnessCase("requests without a token are rejected", LocalApiTests.RequestsWithoutTokenAreRejected)],
        [new HarnessCase("requests are rejected when no token is configured", LocalApiTests.RequestsAreRejectedWhenNoTokenIsConfigured)],
        [new HarnessCase("distinct named tokens authenticate independently and identify their caller", LocalApiTests.DistinctNamedTokensAuthenticateIndependentlyAndIdentifyTheirCaller)],
        [new HarnessCase("revoked token stops authenticating while others still work", LocalApiTests.RevokedTokenStopsAuthenticatingWhileOthersStillWork)],
        [new HarnessCase("memory query endpoint returns matching memories", LocalApiTests.MemoryQueryEndpointReturnsMatchingMemories)],
        [new HarnessCase("RAG query endpoint refuses when dataset has no context", LocalApiTests.RagQueryEndpointRefusesWhenDatasetHasNoContext)],
        [new HarnessCase("chat completion rejects missing fields", LocalApiTests.ChatCompletionRejectsMissingFields)],
        [new HarnessCase("models endpoint returns visible models", LocalApiTests.ModelsEndpointReturnsVisibleModels)],
        [new HarnessCase("embeddings endpoint returns vectors for each input", LocalApiTests.EmbeddingsEndpointReturnsVectorsForEachInput)],
        [new HarnessCase("embeddings endpoint rejects empty input", LocalApiTests.EmbeddingsEndpointRejectsEmptyInput)],
        [new HarnessCase("calls are logged to trace store with caller name", LocalApiTests.CallsAreLoggedToTraceStoreWithCallerName)],
        [new HarnessCase("chat completion applies model profile sampling defaults and honors explicit overrides", LocalApiTests.ChatCompletionAppliesModelProfileSamplingDefaultsAndHonorsExplicitOverrides)]
    ];

    public static IEnumerable<object[]> Voice =>
    [
        [new HarnessCase("phonemizer dictionary words produce only vocab symbols", VoiceTests.PhonemizerDictionaryWordsProduceOnlyVocabSymbols)],
        [new HarnessCase("phonemizer fallback handles out-of-dictionary words", VoiceTests.PhonemizerFallbackHandlesOutOfDictionaryWords)],
        [new HarnessCase("phonemizer is deterministic", VoiceTests.PhonemizerIsDeterministic)],
        [new HarnessCase("tokenizer wraps each chunk with pad tokens", VoiceTests.TokenizerWrapsEachChunkWithPadTokens)],
        [new HarnessCase("tokenizer splits long input into multiple chunks", VoiceTests.TokenizerSplitsLongInputIntoMultipleChunks)],
        [new HarnessCase("tokenizer returns empty for blank input", VoiceTests.TokenizerReturnsEmptyForBlankInput)],
        [new HarnessCase("onnx model refuses to load when assets are missing", VoiceTests.OnnxModelRefusesToLoadWhenAssetsAreMissing)],
        [new HarnessCase("onnx model hash verification rejects tampered file", VoiceTests.OnnxModelHashVerificationRejectsTamperedFile)],
        [new HarnessCase("native provider reports not installed without assets", VoiceTests.NativeProviderReportsNotInstalledWithoutAssets)],
        [new HarnessCase("native provider re-resolves assets root after settings change", VoiceTests.NativeProviderReResolvesAssetsRootAfterSettingsChange)],
        [new HarnessCase("native provider requires no python version", VoiceTests.NativeProviderRequiresNoPythonVersion)]
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

public sealed class McpHarnessTests
{
    [Theory]
    [MemberData(nameof(HarnessCases.Mcp), MemberType = typeof(HarnessCases))]
    public async Task Runs_Mcp_Cases(HarnessCase testCase)
    {
        await testCase.Run();
    }
}

public sealed class LocalApiHarnessTests
{
    [Theory]
    [MemberData(nameof(HarnessCases.LocalApi), MemberType = typeof(HarnessCases))]
    public async Task Runs_LocalApi_Cases(HarnessCase testCase)
    {
        await testCase.Run();
    }
}

public sealed class VoiceHarnessTests
{
    [Theory]
    [MemberData(nameof(HarnessCases.Voice), MemberType = typeof(HarnessCases))]
    public async Task Runs_Voice_Cases(HarnessCase testCase)
    {
        await testCase.Run();
    }
}
