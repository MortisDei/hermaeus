using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.ViewModels;
using Xunit;

namespace Aether.Tests;

public class SessionUsageTests
{
    sealed class FakeConversationStore : IConversationStore
    {
        private readonly List<Conversation> _convs;
        public FakeConversationStore(IEnumerable<Conversation> convs) => _convs = convs.ToList();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<List<Conversation>> GetAllAsync(bool includeArchived = true, System.Threading.CancellationToken ct = default) => Task.FromResult(_convs.ToList());
        public Task<Conversation?> GetByIdAsync(string id, System.Threading.CancellationToken ct = default) => Task.FromResult(_convs.FirstOrDefault(c => c.Id == id));
        public Task SaveAsync(Conversation conversation, System.Threading.CancellationToken ct = default) { return Task.CompletedTask; }
        public Task DeleteAsync(string id, System.Threading.CancellationToken ct = default) { return Task.CompletedTask; }
        public Task<List<Conversation>> SearchAsync(string query, System.Threading.CancellationToken ct = default) => Task.FromResult(_convs.ToList());
    }

    sealed class FakeMemoryStore : IMemoryStore
    {
        private readonly Dictionary<string,int> _counts;
        public FakeMemoryStore(Dictionary<string,int> counts) => _counts = counts;
        public Task InitializeAsync(System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Aether.Core.Models.Memory>> GetAllAsync(bool includeArchived = false, System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();
        public Task<Aether.Core.Models.Memory?> GetByIdAsync(string id, System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();
        public Task<List<Aether.Core.Models.Memory>> GetByCategoryAsync(string category, System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();
        public Task SaveAsync(Aether.Core.Models.Memory memory, System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();
        public Task DeleteAsync(string id, System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();
        public Task<List<Aether.Core.Models.Memory>> SearchAsync(string query, System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();
        public Task<List<Aether.Core.Models.Memory>> GetByImportanceAsync(double minScore, System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();
        public Task<List<Aether.Core.Models.Memory>> GetRecentAsync(int limit = 10, System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();
        public Task<int> GetCountByConversationAsync(string conversationId, bool includeArchived = false, System.Threading.CancellationToken ct = default)
            => Task.FromResult(_counts.TryGetValue(conversationId, out var v) ? v : 0);
        public Task<Dictionary<string,int>> GetCountsByConversationAsync(IEnumerable<string> conversationIds, bool includeArchived = false, System.Threading.CancellationToken ct = default)
            => Task.FromResult(conversationIds.ToDictionary(id => id, id => _counts.TryGetValue(id, out var v) ? v : 0));
    }

    [Fact]
    public async Task RefreshLoadsCounts()
    {
        var convA = new Conversation { Id = "conv-A", Title = "A" };
        var convB = new Conversation { Id = "conv-B", Title = "B" };
        var convs = new[] { convA, convB };
        var counts = new Dictionary<string,int> { ["conv-A"] = 2, ["conv-B"] = 1 };

        var store = new FakeConversationStore(convs);
        var mem = new FakeMemoryStore(counts);
        var toasts = new FakeToasts();

        var vm = new SessionUsageViewModel(store, mem, toasts);
        await vm.RefreshAsync();

        Assert.Equal(2, vm.Items.Count);
        var a = vm.Items.First(i => i.ConversationId == "conv-A");
        var b = vm.Items.First(i => i.ConversationId == "conv-B");
        Assert.Equal(2, a.MemoryCount);
        Assert.Equal(1, b.MemoryCount);
    }
}
