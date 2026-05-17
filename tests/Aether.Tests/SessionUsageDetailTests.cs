using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public class SessionUsageDetailTests
{
    sealed class FakeMemoryStoreFull : IMemoryStore
    {
        private readonly List<Memory> _mems;
        public FakeMemoryStoreFull(IEnumerable<Memory> mems) => _mems = mems.ToList();
        public Task InitializeAsync(System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Memory>> GetAllAsync(bool includeArchived = false, System.Threading.CancellationToken ct = default) => Task.FromResult(_mems.ToList());
        public Task<Memory?> GetByIdAsync(string id, System.Threading.CancellationToken ct = default) => Task.FromResult(_mems.FirstOrDefault(m => m.Id == id));
        public Task<List<Memory>> GetByCategoryAsync(string category, System.Threading.CancellationToken ct = default) => Task.FromResult(_mems.Where(m => m.Category == category).ToList());
        public Task SaveAsync(Memory memory, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Memory>> SearchAsync(string query, System.Threading.CancellationToken ct = default) => Task.FromResult(_mems.Where(m => m.Content.Contains(query)).ToList());
        public Task<List<Memory>> GetByImportanceAsync(double minScore, System.Threading.CancellationToken ct = default) => Task.FromResult(_mems.Where(m => m.ImportanceScore >= minScore).ToList());
        public Task<List<Memory>> GetRecentAsync(int limit = 10, System.Threading.CancellationToken ct = default) => Task.FromResult(_mems.OrderByDescending(m => m.UpdatedAt).Take(limit).ToList());
        public Task<List<Memory>> GetRecentByConversationAsync(string conversationId, int limit = 10, System.Threading.CancellationToken ct = default) => Task.FromResult(_mems.Where(m => m.SourceConversationId == conversationId).OrderByDescending(m => m.UpdatedAt).Take(limit).ToList());
        public Task<int> GetCountByConversationAsync(string conversationId, bool includeArchived = false, System.Threading.CancellationToken ct = default) => Task.FromResult(_mems.Count(m => m.SourceConversationId == conversationId));
        public Task<Dictionary<string,int>> GetCountsByConversationAsync(IEnumerable<string> conversationIds, bool includeArchived = false, System.Threading.CancellationToken ct = default) => Task.FromResult(conversationIds.ToDictionary(id => id, id => _mems.Count(m => m.SourceConversationId == id)));
    }

    [Fact]
    public async Task LoadAndExportWritesFile()
    {
        using var temp = new TempDir();
        var mems = new[]
        {
            new Memory { Id = "m1", Content = "alpha", Category = "facts", CreatedAt = DateTime.UtcNow.AddHours(-2), ImportanceScore = 0.8, SourceConversationId = "conv-X" },
            new Memory { Id = "m2", Content = "beta", Category = "preferences", CreatedAt = DateTime.UtcNow.AddHours(-1), ImportanceScore = 0.3, SourceConversationId = "conv-X" }
        };
        var store = new FakeMemoryStoreFull(mems);
        var settings = NewSettings(temp);
        var vm = new SessionUsageDetailViewModel(store, settings);
        await vm.LoadForConversationAsync("conv-X", "X title");
        Assert.Equal(2, vm.Items.Count);

        var outDir = Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "exports");
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, "export-test.csv");
        await vm.ExportCsvAsync(path);
        Assert.True(File.Exists(path));
        var contents = File.ReadAllText(path);
        Assert.Contains("alpha", contents);
        Assert.Contains("beta", contents);
    }
}
