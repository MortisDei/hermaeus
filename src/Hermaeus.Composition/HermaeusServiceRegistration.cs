using Hermaeus.Agent.Services;
using Hermaeus.Core.Services;
using Hermaeus.Mcp;
using Hermaeus.Rag;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Eval;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.Voice;
using Microsoft.Extensions.DependencyInjection;

namespace Hermaeus.Composition;

/// <summary>
/// Registers the non-UI service graph shared by every Hermaeus host (the
/// Avalonia desktop app and the headless local API). Avalonia-specific
/// registrations (windows, tray, ViewModels) stay in Hermaeus.Desktop; hosts
/// that don't render UI, like Hermaeus.LocalApi, call this and nothing else.
/// </summary>
public static class HermaeusServiceRegistration
{
    public static IServiceCollection AddHermaeusCoreServices(this IServiceCollection s)
    {
        s.AddSingleton<ISettingsService, SettingsService>();
        s.AddSingleton<ISecretStore, SecretStore>();
        s.AddSingleton<RedactionService>();
        s.AddSingleton<IRuntimeLogService, RuntimeLogService>();
        s.AddSingleton<AppLifecycleJournalService>();
        s.AddSingleton<PythonHealthValidator>();
        s.AddSingleton<BackupService>();
        s.AddSingleton<LocalAiSetupService>();
        s.AddSingleton<TrustService>();
        s.AddSingleton<DoctorService>();
        s.AddSingleton<IDoctorService>(sp => sp.GetRequiredService<DoctorService>());
        s.AddSingleton<PrivacyAuditService>();
        s.AddSingleton<ISystemInfoService, SystemInfoService>();
        s.AddSingleton<IEvalStore, SqliteEvalStore>();
        s.AddSingleton<EvalEngine>();
        s.AddSingleton<BenchmarkService>();
        s.AddSingleton<IBenchmarkInsightsService, BenchmarkInsightsService>();
        s.AddSingleton<ITraceStore, SqliteTraceStore>();
        s.AddSingleton<IModelUsageService, ModelUsageService>();
        s.AddSingleton<ChatTraceService>();
        s.AddSingleton<IConversationStore, ConversationStore>();
        s.AddSingleton<IProjectStore, ProjectStore>();
        s.AddSingleton<ConversationExportService>();
        s.AddSingleton<ChatArtifactService>();
        s.AddSingleton<IMemoryStore, MemoryStore>();
        s.AddSingleton<MemoryExtractionService>();
        s.AddSingleton<MemoryInjectionService>();
                s.AddSingleton<IConversationMemoryService, ConversationMemoryService>();
        s.AddSingleton<LlamaCppService>();
        s.AddSingleton<OpenAiService>();
        s.AddSingleton<RuntimeProfileService>();
        s.AddSingleton<OllamaService>();
        s.AddSingleton<ILlmService, CompositeLlmService>();
        s.AddSingleton<ModelProfileService>();
        s.AddSingleton<ModelManifestStore>();
        s.AddSingleton<HuggingFaceClient>();
        s.AddSingleton<ModelDownloadService>();
        s.AddSingleton<XttsV2VoiceProvider>();
        s.AddSingleton<KokoroVoiceProvider>();
        s.AddSingleton<F5TtsVoiceProvider>();
        s.AddSingleton<OpenAiVoiceProvider>();
        s.AddSingleton<NativeKokoroVoiceProvider>();
        s.AddSingleton<IVoiceProviderRegistry, VoiceProviderRegistry>();
        s.AddSingleton<ITtsService, VoiceRoutingTtsService>();
        s.AddSingleton<IVoiceOrchestrator, VoiceOrchestrator>();
        s.AddSingleton<VoiceNotificationBridge>();
        s.AddSingleton<XttsProcessManager>();
        s.AddSingleton<KokoroProcessManager>();
        s.AddSingleton<LocalApiProcessManager>();
        s.AddSingleton<IToastService, ToastService>();
        s.AddSingleton<SqliteRagStore>();
        s.AddSingleton<IEmbeddingService, LlamaCppEmbeddingService>();
        s.AddSingleton<IReranker, OnnxCrossEncoderReranker>();
        s.AddSingleton<RagPipeline>();
        s.AddSingleton<RagQueryService>();
        s.AddSingleton<IAgentRetrievalService, AgentRetrievalService>();
        s.AddSingleton<RagEvalService>();
        s.AddSingleton<IAgentTaskStateStore, FileAgentTaskStateStore>();
        s.AddSingleton<ILessonStore, SqliteLessonStore>();
        s.AddSingleton<IAgentWorkspaceMemoryStore, WorkspaceMemoryStore>();
        s.AddSingleton<IAgentWorkspaceTools, AgentWorkspaceTools>();
        s.AddSingleton<IWorkspaceProfileStore, FileWorkspaceProfileStore>();
        s.AddSingleton<IWorkspaceAnalysisService, WorkspaceAnalysisService>();
        s.AddSingleton<IWorkspaceManifestStore, WorkspaceManifestService>();
        s.AddSingleton<IWorkspaceActivationService, WorkspaceActivationService>();
        s.AddSingleton<IAgentSafetyGate, AgentSafetyGate>();
        s.AddSingleton<IMcpToolBridge, McpToolBridge>();
        s.AddSingleton<IAgentToolExecutor, AgentToolExecutor>();
        s.AddSingleton<IAgentContextBuilder, AgentContextBuilder>();
        s.AddSingleton<IAgentService, AgentService>();
        s.AddSingleton<IPatchDiffService, PatchDiffService>();
        s.AddSingleton<IAgentScenarioStore, AgentScenarioStore>();
        s.AddSingleton<IAgentScenarioRunner, AgentScenarioRunner>();
        return s;
    }
}
