using System.Collections.ObjectModel;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class SessionUsageItemViewModel : ObservableObject
{
    public required string ConversationId { get; init; }
    public required string Title { get; init; }
    [ObservableProperty] private int _memoryCount;
}

public partial class SessionUsageViewModel : ObservableObject
{
    private readonly IConversationStore _conversations;
    private readonly IMemoryStore _memoryStore;
    private readonly IToastService _toasts;

    public ObservableCollection<SessionUsageItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _isLoading;

    public SessionUsageViewModel(IConversationStore conversations, IMemoryStore memoryStore, IToastService toasts)
    {
        _conversations = conversations;
        _memoryStore = memoryStore;
        _toasts = toasts;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var convs = await _conversations.GetAllAsync(includeArchived: true);
            var ids = convs.Select(c => c.Id).ToList();
            var counts = ids.Count > 0
                ? await _memoryStore.GetCountsByConversationAsync(ids, includeArchived: true)
                : new Dictionary<string,int>();

            Items.Clear();
            foreach (var c in convs.OrderByDescending(c => c.UpdatedAt))
            {
                counts.TryGetValue(c.Id, out var count);
                Items.Add(new SessionUsageItemViewModel
                {
                    ConversationId = c.Id,
                    Title = string.IsNullOrWhiteSpace(c.Title) ? "(untitled)" : c.Title,
                    MemoryCount = count
                });
            }
        }
        catch (Exception ex)
        {
            _toasts.Show("Error","Failed to load session usage: " + ex.Message, ToastKind.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
