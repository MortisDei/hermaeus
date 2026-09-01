using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermaeus.Services;

namespace Hermaeus.ViewModels;

/// <summary>
/// ViewModel for managing and displaying memories in the UI.
/// </summary>
public partial class MemoriesViewModel : ViewModelBase
{
    private static readonly RedactionService ExportRedactor = new();
    private readonly IMemoryStore _store;
    private readonly IKnowledgeRevisionStore _knowledge;
    private readonly IConversationStore _conversations;
    private readonly ISettingsService _settings;
    private readonly IToastService _toasts;
    private readonly IActivityRecorder? _activity;
    private readonly ActivityViewModel? _activityViewModel;
    private CancellationTokenSource? _searchTextCts;

    public UiBoundCollection<MemoryItemViewModel> Memories { get; } = [];
    public UiBoundCollection<MemoryRevisionItemViewModel> RevisionTimeline { get; } = [];
    public UiBoundCollection<KnowledgeContradictionProposalViewModel> ContradictionProposals { get; } = [];

    /// <summary>Per-conversation memory counts, for triaging where memory sprawl is
    /// coming from. Replaces the standalone Session Usage panel (Feature Audit: Merge).</summary>
    public UiBoundCollection<ConversationFilterItemViewModel> ConversationFilters { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = string.Empty;  // Filter by category
    [ObservableProperty] private ConversationFilterItemViewModel? _selectedConversationFilter;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _embeddingMismatchCount;
    [ObservableProperty] private bool _isReembedding;
    [ObservableProperty] private MemoryItemViewModel? _selectedMemory;
    [ObservableProperty] private string _revisionDraftContent = string.Empty;
    [ObservableProperty] private bool _isRevisionBusy;
    [ObservableProperty] private MemoryItemViewModel? _contradictionTarget;
    [ObservableProperty] private string _contradictionExplanation = string.Empty;
    [ObservableProperty] private bool _isActivityExpanded;

    public List<string> AvailableCategories { get; } = ["All", "facts", "preferences", "learned_behaviors", "interests"];
    public Func<MemoryItemViewModel, Task<bool>>? RequestDeleteConfirmation { get; set; }

    public MemoriesViewModel(
        IMemoryStore store,
        IConversationStore conversations,
        ISettingsService settings,
        IToastService toasts,
        IActivityRecorder? activity = null,
        IKnowledgeRevisionStore? knowledge = null,
        ActivityViewModel? activityViewModel = null)
    {
        _activity = activity;
        _store = store;
        _knowledge = knowledge ?? store as IKnowledgeRevisionStore
            ?? throw new ArgumentException("The memory store must expose knowledge revision writes.", nameof(store));
        _conversations = conversations;
        _settings = settings;
        _toasts = toasts;
        _activityViewModel = activityViewModel;
        _selectedCategory = "All";
        Memories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoMemories));
        ContradictionProposals.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasContradictionProposals));
    }

    public bool HasNoMemories => Memories.Count == 0;
    public bool HasSelectedMemory => SelectedMemory is not null;
    public bool HasContradictionProposals => ContradictionProposals.Count > 0;
    public bool HasActivity => _activityViewModel is not null;
    public ActivityViewModel? Activity => _activityViewModel;

    [RelayCommand]
    public async Task InitializeAsync()
    {
        // Best-effort: sweep memories that have decayed below the floor and
        // gone unrecalled long enough into the archive before loading the
        // list, so the panel reflects lifecycle state without needing a
        // separate background job.
        // r28 doc 03 3.3: a sweep that archives nothing still records, because
        // "it ran and found nothing" and "it never ran" are the two states
        // this panel exists to separate.
        try
        {
            var archived = await _store.ArchiveStaleMemoriesAsync();
            _activity.RecordSafe("memory.auto-archive", string.Empty, ActivityOutcome.Succeeded,
                archived == 1 ? "Archived 1 stale memory" : $"Archived {archived} stale memories");
        }
        catch (Exception ex)
        {
            _activity.RecordSafe("memory.auto-archive", string.Empty, ActivityOutcome.Failed,
                "Memory archive sweep failed", ex.Message);
        }

        await LoadMemoriesAsync();
        await RefreshConversationFiltersAsync();
        await RefreshEmbeddingMismatchAsync();
        await RefreshContradictionProposalsAsync();
        if (_activityViewModel is not null)
            await _activityViewModel.RefreshAsync();
    }

    /// <summary>doc 04 4.1: registered next to the ViewModel that owns the action.</summary>
    public void RegisterCommands(ICommandRegistry registry)
    {
        registry.Register(new AppCommand(
            Id: "memories.search", Title: "Search memories", Area: "Memory",
            Description: "Search stored memories by text.",
            Keywords: ["memory", "search", "find"], Shortcut: "",
            CanExecute: () => true,
            Execute: () => SearchCommand.ExecuteAsync(null)));

        registry.Register(new AppCommand(
            Id: "memories.reembed-mismatched", Title: "Re-embed mismatched memories", Area: "Memory",
            Description: "Re-embed memories whose vectors came from a different embedding model.",
            Keywords: ["memory", "embedding", "reembed", "mismatch"], Shortcut: "",
            CanExecute: () => EmbeddingMismatchCount > 0,
            DisabledReason: () => "No mismatched embeddings.",
            Execute: () => ReembedMismatchedCommand.ExecuteAsync(null)));
    }

    /// <summary>
    /// Surfaces the "old vectors after an embedding model switch" gap (r16
    /// 02-memory-integrity.md 2.4): recall degrades silently to FTS-only
    /// otherwise, indistinguishable from working. Best-effort; a failed
    /// probe (no embedding service reachable) just shows no banner.
    /// </summary>
    [RelayCommand]
    public async Task RefreshEmbeddingMismatchAsync()
    {
        try { EmbeddingMismatchCount = await _store.GetEmbeddingMismatchCountAsync(); }
        catch { EmbeddingMismatchCount = 0; }
    }

    public bool HasEmbeddingMismatch => EmbeddingMismatchCount > 0;

    public string EmbeddingMismatchLabel =>
        $"{EmbeddingMismatchCount} memor{(EmbeddingMismatchCount == 1 ? "y was" : "ies were")} embedded with a different model.";

    partial void OnEmbeddingMismatchCountChanged(int value) => OnPropertyChanged(nameof(HasEmbeddingMismatch));

    /// <summary>
    /// User-clicked only (r16 02-memory-integrity.md 2.4 explicit rejection:
    /// no automatic re-embed on a model switch). Clears the stale vectors
    /// and kicks off a background backfill; the mismatch count is refreshed
    /// immediately (it drops to 0 as soon as the clear completes) rather
    /// than waiting for the backfill to finish.
    /// </summary>
    [RelayCommand]
    public async Task ReembedMismatchedAsync()
    {
        IsReembedding = true;
        try
        {
            var cleared = await _store.ClearMismatchedEmbeddingsAsync();
            await RefreshEmbeddingMismatchAsync();
            _toasts.Show("Re-embedding started", $"Cleared {cleared} stale embedding(s); they will be re-embedded in the background.", ToastKind.Info);
        }
        catch (Exception ex)
        {
            _toasts.Show("Error", $"Failed to re-embed memories: {ex.Message}", ToastKind.Error);
        }
        finally
        {
            IsReembedding = false;
        }
    }

    [RelayCommand]
    public async Task RefreshConversationFiltersAsync()
    {
        try
        {
            var convs = await _conversations.GetAllAsync(includeArchived: true);
            var ids = convs.Select(c => c.Id).ToList();
            var counts = ids.Count > 0
                ? await _store.GetCountsByConversationAsync(ids, includeArchived: true)
                : new Dictionary<string, int>();

            var previouslySelected = SelectedConversationFilter?.ConversationId;
            ConversationFilters.Clear();
            foreach (var c in convs.OrderByDescending(c => c.UpdatedAt))
            {
                counts.TryGetValue(c.Id, out var count);
                ConversationFilters.Add(new ConversationFilterItemViewModel
                {
                    ConversationId = c.Id,
                    Title = string.IsNullOrWhiteSpace(c.Title) ? "(untitled)" : c.Title,
                    MemoryCount = count
                });
            }

            SelectedConversationFilter = previouslySelected is null
                ? null
                : ConversationFilters.FirstOrDefault(f => f.ConversationId == previouslySelected);
        }
        catch (Exception ex)
        {
            _toasts.Show("Error", $"Failed to load conversation list: {ex.Message}", ToastKind.Error);
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        IsLoading = true;
        try
        {
            List<Memory> results;

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                results = SelectedCategory == "All" || SelectedCategory == string.Empty
                    ? await _store.GetAllAsync(includeArchived: false)
                    : await _store.GetByCategoryAsync(SelectedCategory);
            }
            else
            {
                results = await _store.SearchAsync(SearchText);
                if (SelectedCategory != "All" && !string.IsNullOrWhiteSpace(SelectedCategory))
                    results = results.Where(m => m.Category == SelectedCategory).ToList();
            }

            if (SelectedConversationFilter is not null)
                results = results.Where(m => string.Equals(m.SourceConversationId, SelectedConversationFilter.ConversationId, StringComparison.Ordinal)).ToList();

            Memories.Clear();
            foreach (var memory in results.OrderByDescending(m => m.IsPinned).ThenByDescending(m => MemoryLifecycle.ComputeEffectiveImportance(m)))
            {
                Memories.Add(ToViewModel(memory));
            }

            TotalCount = Memories.Count;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportConversationCsv))]
    public async Task ExportConversationCsvAsync()
    {
        if (SelectedConversationFilter is null) return;

        var outDir = Path.Combine(Hermaeus.Services.SettingsService.ResolveDataRoot(_settings.Settings), "exports");
        var file = Path.Combine(outDir, $"memories-{SelectedConversationFilter.ConversationId}-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");

        var lines = new List<string> { "Id,Category,CreatedAt,Importance,Content" };
        foreach (var m in Memories)
        {
            var safe = m.Content.Replace("\r", " ").Replace("\n", " ").Replace(",", " ");
            lines.Add($"{m.Id},{m.Category},{m.CreatedAt:o},{m.ImportanceScore},{safe}");
        }

        await AtomicFile.WriteAllTextAsync(file, string.Join(Environment.NewLine, lines));
        _toasts.Show("Exported", $"Wrote {Memories.Count} memories to {file}", ToastKind.Success);
    }

    private bool CanExportConversationCsv() => SelectedConversationFilter is not null;

    [RelayCommand]
    public async Task ExportMemoryHistoryJsonAsync()
    {
        var assertions = new List<VersionedMemoryAssertionExport>();
        foreach (var memory in Memories)
        {
            var history = await _knowledge.GetHistoryAsync(memory.Id);
            if (history.Count == 0) continue;
            assertions.Add(new VersionedMemoryAssertionExport(
                memory.Id,
                history.Select(ToVersionedMemoryRevisionExport).ToList()));
        }

        var export = new VersionedMemoryExport(1, DateTime.UtcNow, assertions);
        var outDir = Path.Combine(SettingsService.ResolveDataRoot(_settings.Settings), "exports");
        var file = Path.Combine(outDir, $"memories-history-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.json");
        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
        await AtomicFile.WriteAllTextAsync(file, json);
        _toasts.Show("Exported", $"Wrote {assertions.Count} memory histories to {file}", ToastKind.Success);
    }

    [RelayCommand]
    public void ClearConversationFilter()
    {
        SelectedConversationFilter = null;
    }

    [RelayCommand]
    public async Task DeleteMemoryAsync(string memoryId)
    {
        try
        {
            var item = Memories.FirstOrDefault(m => m.Id == memoryId);
            if (item is null || RequestDeleteConfirmation is null || !await RequestDeleteConfirmation(item))
                return;
            var revision = await _knowledge.GetCurrentRevisionAsync(memoryId);
            if (revision is not null)
                await _knowledge.HardDeleteAsync(memoryId, revision.RevisionId);
            Memories.Remove(item);
            _toasts.Show("Memory deleted", "The memory has been removed.", ToastKind.Info);
        }
        catch (Exception ex)
        {
            _toasts.Show("Error", $"Failed to delete memory: {ex.Message}", ToastKind.Error);
        }
    }

    [RelayCommand]
    public async Task InspectMemoryAsync(string memoryId)
    {
        var memory = await _store.GetByIdAsync(memoryId);
        if (memory is null)
        {
            SelectedMemory = null;
            RevisionTimeline.Clear();
            return;
        }

        var revision = await _knowledge.GetCurrentRevisionAsync(memoryId);
        if (revision is null) return;
        var history = await _knowledge.GetHistoryAsync(memoryId);
        SelectedMemory = ToViewModel(memory);
        RevisionDraftContent = memory.Content;
        RevisionTimeline.Clear();
        for (var index = 0; index < history.Count; index++)
        {
            var previous = index + 1 < history.Count ? history[index + 1] : null;
            RevisionTimeline.Add(ToRevisionViewModel(history[index], previous));
        }
        OnPropertyChanged(nameof(HasSelectedMemory));
    }

    [RelayCommand]
    public async Task RefreshContradictionProposalsAsync()
    {
        try
        {
            var proposals = await _knowledge.GetContradictionProposalsAsync();
            ContradictionProposals.Clear();
            foreach (var proposal in proposals)
                ContradictionProposals.Add(ToContradictionProposalViewModel(proposal));
        }
        catch (Exception ex)
        {
            _toasts.Show("Error", $"Failed to load contradiction proposals: {ex.Message}", ToastKind.Error);
        }
    }

    [RelayCommand]
    public async Task ProposeContradictionAsync()
    {
        if (SelectedMemory is null || ContradictionTarget is null
            || string.Equals(SelectedMemory.Id, ContradictionTarget.Id, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(ContradictionExplanation))
            return;

        IsRevisionBusy = true;
        try
        {
            var left = await _knowledge.GetCurrentRevisionAsync(SelectedMemory.Id);
            var right = await _knowledge.GetCurrentRevisionAsync(ContradictionTarget.Id);
            if (left is null || right is null) return;
            await _knowledge.CreateContradictionProposalAsync(new KnowledgeContradictionProposalDraft(
                left.AssertionId,
                left.RevisionId,
                right.AssertionId,
                right.RevisionId,
                ContradictionExplanation.Trim(),
                "Compare the exact source references shown in each revision timeline.",
                "Compare the exact recorded and effective times shown in each revision timeline.",
                KnowledgeContradictionDisposition.Coexist,
                "Owner review is required before either assertion can be changed."));
            ContradictionExplanation = string.Empty;
            await RefreshContradictionProposalsAsync();
            _toasts.Show("Proposal recorded", "Neither memory was changed. Review the pending contradiction below.", ToastKind.Info);
        }
        finally
        {
            IsRevisionBusy = false;
        }
    }

    [RelayCommand]
    public async Task RejectContradictionProposalAsync(string proposalId)
    {
        IsRevisionBusy = true;
        try
        {
            await _knowledge.RejectContradictionProposalAsync(proposalId,
                new KnowledgeRevisionDecision("reject-contradiction", "owner",
                    "Rejected from Memories review; neither assertion was changed.", DateTime.UtcNow));
            await RefreshContradictionProposalsAsync();
            _toasts.Show("Proposal rejected", "Both exact revisions remain available in their timelines.", ToastKind.Info);
        }
        finally
        {
            IsRevisionBusy = false;
        }
    }

    [RelayCommand]
    public async Task ReviseSelectedAsync()
    {
        await WriteSelectedRevisionAsync(correct: false);
    }

    [RelayCommand]
    public async Task CorrectSelectedAsync()
    {
        await WriteSelectedRevisionAsync(correct: true);
    }

    [RelayCommand]
    public async Task MarkSelectedDisputedAsync()
    {
        if (SelectedMemory is null) return;
        IsRevisionBusy = true;
        try
        {
            var current = await _knowledge.GetCurrentRevisionAsync(SelectedMemory.Id);
            if (current is null) return;
            await _knowledge.SetDisputeAsync(SelectedMemory.Id, current.RevisionId, true,
                new KnowledgeRevisionDecision("dispute", "owner", "Marked disputed from Memories review.", DateTime.UtcNow));
            await InspectMemoryAsync(SelectedMemory.Id);
        }
        finally
        {
            IsRevisionBusy = false;
        }
    }

    [RelayCommand]
    public async Task RestoreRevisionAsync(string revisionId)
    {
        if (SelectedMemory is null) return;
        IsRevisionBusy = true;
        try
        {
            var current = await _knowledge.GetCurrentRevisionAsync(SelectedMemory.Id);
            if (current is null) return;
            await _knowledge.RestoreRevisionAsync(SelectedMemory.Id, current.RevisionId, revisionId,
                new KnowledgeRevisionDecision("restore", "owner", "Restored from Memories review.", DateTime.UtcNow));
            await SearchAsync();
            await InspectMemoryAsync(SelectedMemory.Id);
        }
        finally
        {
            IsRevisionBusy = false;
        }
    }

    [RelayCommand]
    public void ClearMemoryInspection()
    {
        SelectedMemory = null;
        RevisionTimeline.Clear();
        RevisionDraftContent = string.Empty;
        OnPropertyChanged(nameof(HasSelectedMemory));
    }

    private async Task WriteSelectedRevisionAsync(bool correct)
    {
        if (SelectedMemory is null || string.IsNullOrWhiteSpace(RevisionDraftContent)) return;
        IsRevisionBusy = true;
        try
        {
            var memory = await _store.GetByIdAsync(SelectedMemory.Id);
            var current = await _knowledge.GetCurrentRevisionAsync(SelectedMemory.Id);
            if (memory is null || current is null) return;
            memory.Content = RevisionDraftContent.Trim();
            var draft = new KnowledgeRevisionDraft(
                memory,
                TemporalOrigin: KnowledgeTemporalOrigin.UserProvided,
                Decision: new KnowledgeRevisionDecision(
                    correct ? "correction" : "revision",
                    "owner",
                    correct ? "Corrected from Memories review." : "Revised from Memories review.",
                    DateTime.UtcNow));
            if (correct)
                await _knowledge.CorrectAssertionAsync(memory.Id, current.RevisionId, draft);
            else
                await _knowledge.ReviseAssertionAsync(memory.Id, current.RevisionId, draft);
            await SearchAsync();
            await InspectMemoryAsync(memory.Id);
        }
        finally
        {
            IsRevisionBusy = false;
        }
    }

    [RelayCommand]
    public async Task TogglePinAsync(string memoryId)
    {
        try
        {
            var memory = await _store.GetByIdAsync(memoryId);
            if (memory is null) return;

            memory.IsPinned = !memory.IsPinned;
            var revision = await _knowledge.GetCurrentRevisionAsync(memoryId);
            if (revision is null) return;
            await _knowledge.MutatePresentationAsync(memoryId, revision.RevisionId,
                KnowledgePresentationMutation.FromMemory(memory));

            var item = Memories.FirstOrDefault(m => m.Id == memoryId);
            if (item is not null)
                item.IsPinned = memory.IsPinned;

            _toasts.Show(memory.IsPinned ? "Memory pinned" : "Memory unpinned", "", ToastKind.Info);
        }
        catch (Exception ex)
        {
            _toasts.Show("Error", $"Failed to update memory: {ex.Message}", ToastKind.Error);
        }
    }

    [RelayCommand]
    public async Task ToggleArchiveAsync(string memoryId)
    {
        try
        {
            var memory = await _store.GetByIdAsync(memoryId);
            if (memory is null) return;

            memory.IsArchived = !memory.IsArchived;
            var revision = await _knowledge.GetCurrentRevisionAsync(memoryId);
            if (revision is null) return;
            await _knowledge.MutatePresentationAsync(memoryId, revision.RevisionId,
                KnowledgePresentationMutation.FromMemory(memory));

            var item = Memories.FirstOrDefault(m => m.Id == memoryId);
            if (item is not null)
                Memories.Remove(item);  // Remove from view if archiving

            _toasts.Show(memory.IsArchived ? "Memory archived" : "Memory restored", "", ToastKind.Info);
        }
        catch (Exception ex)
        {
            _toasts.Show("Error", $"Failed to update memory: {ex.Message}", ToastKind.Error);
        }
    }

    private async Task LoadMemoriesAsync()
    {
        IsLoading = true;
        try
        {
            var memories = await _store.GetAllAsync(includeArchived: false);
            Memories.Clear();
            foreach (var memory in memories.OrderByDescending(m => m.IsPinned).ThenByDescending(m => MemoryLifecycle.ComputeEffectiveImportance(m)))
            {
                Memories.Add(ToViewModel(memory));
            }
            TotalCount = Memories.Count;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// r12 02-async-and-threading.md 2.3: debounces per-keystroke search
    /// (one DB search per character otherwise, with unordered completion
    /// interleaving Clear/Add on the bound <see cref="Memories"/>
    /// collection) using the same 300 ms + CTS shape as
    /// <see cref="MainWindowViewModel.OnSearchQueryChanged"/>.
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchTextCts?.Cancel();
        _searchTextCts?.Dispose();
        _searchTextCts = new CancellationTokenSource();
        var token = _searchTextCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                if (token.IsCancellationRequested) return;
                await RunOnUiAsync(SearchAsync);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        SearchCommand.Execute(null);
    }

    partial void OnSelectedConversationFilterChanged(ConversationFilterItemViewModel? value)
    {
        ExportConversationCsvCommand.NotifyCanExecuteChanged();
        SearchCommand.Execute(null);
    }

    private static MemoryItemViewModel ToViewModel(Memory memory) => new()
    {
        Id = memory.Id,
        Category = memory.Category,
        Content = memory.Content,
        CreatedAt = memory.CreatedAt,
        UpdatedAt = memory.UpdatedAt,
        IsPinned = memory.IsPinned,
        IsArchived = memory.IsArchived,
        ImportanceScore = memory.ImportanceScore,
        EffectiveImportance = MemoryLifecycle.ComputeEffectiveImportance(memory),
        RecallCount = memory.RecallCount,
        LastRecalledAt = memory.LastRecalledAt,
        Tags = string.Join(", ", memory.Tags),
        FrequencyCount = memory.FrequencyCount
    };

    private static MemoryRevisionItemViewModel ToRevisionViewModel(
        KnowledgeAssertionRevision revision,
        KnowledgeAssertionRevision? previous) => new()
    {
        RevisionId = revision.RevisionId,
        PreviousRevisionId = revision.PreviousRevisionId,
        Content = revision.Content,
        Status = revision.Status,
        RecordedAt = revision.RecordedAtUtc,
        EffectiveFrom = revision.EffectiveFromUtc,
        EffectiveTo = revision.EffectiveToUtc,
        DiffDisplay = BuildRevisionDiff(revision.Content, previous?.Content),
        Decision = revision.Decision is null
            ? "No decision recorded"
            : $"{revision.Decision.Kind} by {revision.Decision.Actor}: {revision.Decision.Reason}",
        Sources = revision.SourceReferences.Count == 0
            ? "No source references"
            : string.Join(", ", revision.SourceReferences.Select(source => source.Title))
    };

    private static string BuildRevisionDiff(string current, string? previous)
    {
        if (previous is null)
            return "Diff: initial revision";
        if (string.Equals(current, previous, StringComparison.Ordinal))
            return "Diff: content unchanged";

        static string Preview(string value)
        {
            var compact = value.ReplaceLineEndings(" ").Trim();
            return compact.Length <= 180 ? compact : $"{compact[..180]}...";
        }

        return $"Diff: - {Preview(previous)} + {Preview(current)}";
    }

    private static VersionedMemoryRevisionExport ToVersionedMemoryRevisionExport(KnowledgeAssertionRevision revision) =>
        new(
            revision.RevisionId,
            revision.PreviousRevisionId,
            RedactAndBound(revision.Content, 65536),
            revision.Scope,
            RedactAndBound(revision.ScopeId, 2048),
            RedactAndBound(revision.Category, 128),
            revision.RecordedAtUtc,
            revision.EffectiveFromUtc,
            revision.EffectiveToUtc,
            revision.TemporalOrigin,
            revision.SourceReferences.Select(source => new VersionedMemorySourceExport(
                source.Kind,
                RedactAndBound(source.Title, 512),
                RedactAndBound(source.Locator, 2048),
                RedactAndBound(source.Snippet, 4096),
                source.Score,
                source.Timestamp,
                source.EvidenceOrigin)).ToList(),
            revision.Status,
            revision.Decision is null
                ? null
                : new VersionedMemoryDecisionExport(
                    RedactAndBound(revision.Decision.Kind, 128),
                    RedactAndBound(revision.Decision.Actor, 128),
                    RedactAndBound(revision.Decision.Reason, 2048),
                    revision.Decision.RecordedAtUtc,
                    revision.Decision.DecisionId));

    private static KnowledgeContradictionProposalViewModel ToContradictionProposalViewModel(
        KnowledgeContradictionProposal proposal) => new()
        {
            ProposalId = proposal.ProposalId,
            LeftRevision = $"{proposal.LeftAssertionId} / {proposal.LeftRevisionId}",
            RightRevision = $"{proposal.RightAssertionId} / {proposal.RightRevisionId}",
            Explanation = proposal.Explanation,
            SourceComparison = proposal.SourceComparison,
            EffectiveTimeComparison = proposal.EffectiveTimeComparison,
            MissingEvidence = proposal.MissingEvidence,
            Status = proposal.Status,
            Decision = proposal.Decision is null
                ? "Pending owner review"
                : $"{proposal.Decision.Kind} by {proposal.Decision.Actor}: {proposal.Decision.Reason}"
        };

    private static string? RedactAndBound(string? value, int maximum)
    {
        if (value is null) return null;
        var redacted = ExportRedactor.Redact(value);
        return redacted[..Math.Min(redacted.Length, maximum)];
    }

    private sealed record VersionedMemoryExport(
        int Version,
        DateTime ExportedAtUtc,
        IReadOnlyList<VersionedMemoryAssertionExport> Assertions);

    private sealed record VersionedMemoryAssertionExport(
        string AssertionId,
        IReadOnlyList<VersionedMemoryRevisionExport> Revisions);

    private sealed record VersionedMemoryRevisionExport(
        string RevisionId,
        string? PreviousRevisionId,
        string? Content,
        MemoryScope Scope,
        string? ScopeId,
        string? Category,
        DateTime RecordedAtUtc,
        DateTime? EffectiveFromUtc,
        DateTime? EffectiveToUtc,
        KnowledgeTemporalOrigin TemporalOrigin,
        IReadOnlyList<VersionedMemorySourceExport> Sources,
        KnowledgeRevisionStatus Status,
        VersionedMemoryDecisionExport? Decision);

    private sealed record VersionedMemorySourceExport(
        ProvenanceKind Kind,
        string? Title,
        string? Locator,
        string? Snippet,
        double? Score,
        DateTime? Timestamp,
        EvidenceOrigin EvidenceOrigin);

    private sealed record VersionedMemoryDecisionExport(
        string? Kind,
        string? Actor,
        string? Reason,
        DateTime RecordedAtUtc,
        string? DecisionId);
}

/// <summary>
/// ViewModel for a single memory item in the UI.
/// </summary>
public partial class MemoryItemViewModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Content { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public required double ImportanceScore { get; init; }
    public double EffectiveImportance { get; init; }
    public int RecallCount { get; init; }
    public DateTime? LastRecalledAt { get; init; }
    public string Tags { get; init; } = string.Empty;
    public int FrequencyCount { get; init; } = 1;

    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isArchived;

    public string CategoryDisplay => Category switch
    {
        "facts" => "📌 Fact",
        "preferences" => "❤️ Preference",
        "learned_behaviors" => "🧠 Learned",
        "interests" => "✨ Interest",
        _ => Category
    };

    public string CreatedDisplay
    {
        get
        {
            var local = CreatedAt.Kind == DateTimeKind.Utc ? CreatedAt.ToLocalTime() : CreatedAt;
            var today = DateTime.Today;
            return local.Date == today
                ? local.ToString("HH:mm")
                : local.Date >= today.AddDays(-7)
                    ? local.ToString("ddd")
                    : local.ToString("d MMM");
        }
    }

    public string RecallDisplay => LastRecalledAt is null
        ? "Never recalled"
        : $"Recalled {RecallCount}x, last {LastRecalledAt.Value.ToLocalTime():d MMM}";

    public string ImportanceDisplay => ImportanceScore switch
    {
        >= 0.8 => "Very Important",
        >= 0.6 => "Important",
        >= 0.4 => "Medium",
        _ => "Low"
    };

    public string PinButtonLabel => IsPinned ? "Unpin" : "Pin";
    public string PinStateLabel => IsPinned ? "Pinned" : "Not pinned";

    partial void OnIsPinnedChanged(bool value)
    {
        OnPropertyChanged(nameof(PinButtonLabel));
        OnPropertyChanged(nameof(PinStateLabel));
    }
}

public sealed class MemoryRevisionItemViewModel
{
    public required string RevisionId { get; init; }
    public string? PreviousRevisionId { get; init; }
    public required string Content { get; init; }
    public required KnowledgeRevisionStatus Status { get; init; }
    public required DateTime RecordedAt { get; init; }
    public DateTime? EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public required string DiffDisplay { get; init; }
    public required string Decision { get; init; }
    public required string Sources { get; init; }
    public string StatusDisplay => Status.ToString();
    public string RecordedDisplay => $"Recorded {RecordedAt.ToLocalTime():g}";
    public string EffectiveDisplay => EffectiveFrom is null
        ? "Effective time: Unknown"
        : EffectiveTo is null
            ? $"Effective from {EffectiveFrom.Value.ToLocalTime():g}"
            : $"Effective {EffectiveFrom.Value.ToLocalTime():g} to {EffectiveTo.Value.ToLocalTime():g}";
}

public sealed class KnowledgeContradictionProposalViewModel
{
    public required string ProposalId { get; init; }
    public required string LeftRevision { get; init; }
    public required string RightRevision { get; init; }
    public required string Explanation { get; init; }
    public required string SourceComparison { get; init; }
    public required string EffectiveTimeComparison { get; init; }
    public required string MissingEvidence { get; init; }
    public required KnowledgeContradictionProposalStatus Status { get; init; }
    public required string Decision { get; init; }
    public string StatusDisplay => Status.ToString();
}

/// <summary>
/// One entry in the conversation filter list: a conversation and how many
/// memories it has contributed, used to triage memory sprawl per-conversation.
/// </summary>
public partial class ConversationFilterItemViewModel : ObservableObject
{
    public required string ConversationId { get; init; }
    public required string Title { get; init; }
    [ObservableProperty] private int _memoryCount;

    public string Display => $"{Title} ({MemoryCount})";
}
