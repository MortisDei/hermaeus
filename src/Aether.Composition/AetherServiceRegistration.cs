using Aether.Agent.Services;
using Aether.Core.Services;
using Aether.Mcp;
using Aether.Rag;
using Aether.Rag.Embeddings;
using Aether.Rag.Eval;
using Aether.Rag.Pipeline;
using Aether.Rag.Retrieval;
using Aether.Rag.Storage;
using Aether.Services;
using Aether.Services.ProcessManagement;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.Composition;

/// <summary>
/// Registers the non-UI service graph shared by every Aether host (the
/// Avalonia desktop app and the headless local API). Avalonia-specific
/// registrations (windows, tray, ViewModels) stay in Aether.Desktop; hosts
/// that don't render UI, like Aether.LocalApi, call this and nothing else.
/// </summary>
public static class AetherServiceRegistration
{
    public static IServiceCollection AddAetherCoreServices(this IServiceCollection s)
    {
        s.AddSingleton<ISettingsService, SettingsService>();
        s.AddSingleton<ISecretStore, SecretStore>();
        s.AddSingleton<IRedactionService, RedactionService>();
        s.AddSingleton<IRuntimeLogService, RuntimeLogService>();
        s.AddSingleton<PythonHealthValidator>();
        s.AddSingleton<IBackupService, BackupService>();
        s.AddSingleton<ILocalAiSetupService, LocalAiSetupService>();
        s.AddSingleton<TrustService>();
        s.AddSingleton<ITrustService>(sp => sp.GetRequiredService<TrustService>());
        s.AddSingleton<IInspectionCheckProvider>(sp => sp.GetRequiredService<TrustService>());
        s.AddSingleton<DoctorService>();
        s.AddSingleton<IDoctorService>(sp => sp.GetRequiredService<DoctorService>());
        s.AddSingleton<IInspectionCheckProvider>(sp => sp.GetRequiredService<DoctorService>());
        s.AddSingleton<PrivacyAuditService>();
        s.AddSingleton<IPrivacyAuditService>(sp => sp.GetRequiredService<PrivacyAuditService>());
        s.AddSingleton<IInspectionCheckProvider>(sp => sp.GetRequiredService<PrivacyAuditService>());
        s.AddSingleton<IInspectionEngine, InspectionEngine>();
        s.AddSingleton<ISystemInfoService, SystemInfoService>();
        s.AddSingleton<IEvalStore, SqliteEvalStore>();
        s.AddSingleton<IEvalEngine, EvalEngine>();
        s.AddSingleton<IBenchmarkService, BenchmarkService>();
        s.AddSingleton<ITraceStore, SqliteTraceStore>();
        s.AddSingleton<IConversationStore, ConversationStore>();
        s.AddSingleton<IConversationExportService, ConversationExportService>();
        s.AddSingleton<IMemoryStore, MemoryStore>();
        s.AddSingleton<IMemoryExtractionService, MemoryExtractionService>();
        s.AddSingleton<IMemoryInjectionService, MemoryInjectionService>();
        s.AddSingleton<IConversationMemoryService, ConversationMemoryService>();
        s.AddSingleton<LlamaCppService>();
        s.AddSingleton<OpenAiService>();
        s.AddSingleton<IRuntimeProfileService, RuntimeProfileService>();
        s.AddSingleton<OllamaService>();
        s.AddSingleton<ILlmService, CompositeLlmService>();
        s.AddSingleton<IModelProfileService, ModelProfileService>();
        s.AddSingleton<XttsV2VoiceProvider>();
        s.AddSingleton<KokoroVoiceProvider>();
        s.AddSingleton<F5TtsVoiceProvider>();
        s.AddSingleton<OpenAiVoiceProvider>();
        s.AddSingleton<IVoiceProviderRegistry, VoiceProviderRegistry>();
        s.AddSingleton<ITtsService, VoiceRoutingTtsService>();
        s.AddSingleton<XttsProcessManager>();
        s.AddSingleton<KokoroProcessManager>();
        s.AddSingleton<IToastService, ToastService>();
        s.AddSingleton<SqliteRagStore>();
        s.AddSingleton<IEmbeddingService, LlamaCppEmbeddingService>();
        s.AddSingleton<IReranker, OnnxCrossEncoderReranker>();
        s.AddSingleton<RagPipeline>();
        s.AddSingleton<RagQueryService>();
        s.AddSingleton<IAgentRetrievalService, AgentRetrievalService>();
        s.AddSingleton<RagEvalService>();
        s.AddSingleton<IAgentTaskStateStore, FileAgentTaskStateStore>();
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
        return s;
    }
}
