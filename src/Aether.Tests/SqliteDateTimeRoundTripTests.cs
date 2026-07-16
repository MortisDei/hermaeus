using Aether.Core.Models;
using Aether.Services;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>
/// r11 3.4: MemoryStore/ConversationStore parsed stored round-trip ("O")
/// timestamps with plain DateTime.Parse. Without DateTimeStyles.RoundtripKind,
/// a "...Z" (UTC) string is silently converted to Local-kind on read, which
/// then throws off every downstream comparison against DateTime.UtcNow
/// (staleness windows, decay, dedupe cutoffs) by the machine's UTC offset.
/// </summary>
public sealed class SqliteDateTimeRoundTripTests
{
    [Fact]
    public void SqliteDateTime_Parse_preserves_Utc_kind_and_the_exact_instant()
    {
        var original = new DateTime(2026, 3, 15, 12, 30, 45, DateTimeKind.Utc);
        var roundTripped = SqliteDateTime.Parse(original.ToString("O"));

        Assert.Equal(DateTimeKind.Utc, roundTripped.Kind);
        Assert.Equal(original, roundTripped);
        Assert.Equal(original.Ticks, roundTripped.Ticks);
    }

    [Fact]
    public async Task MemoryStore_round_trips_timestamps_as_Utc()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new MemoryStore(settings);
        await store.InitializeAsync();

        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await store.SaveAsync(new Memory { Id = "m1", Content = "note", CreatedAt = created });

        var reloaded = await store.GetByIdAsync("m1");

        Assert.Equal(DateTimeKind.Utc, reloaded!.CreatedAt.Kind);
        Assert.Equal(created, reloaded.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, reloaded.UpdatedAt.Kind);
    }

    [Fact]
    public async Task ConversationStore_round_trips_timestamps_as_Utc()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new ConversationStore(settings);
        await store.InitializeAsync();

        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await store.SaveAsync(new Conversation { Id = "c1", Title = "test", CreatedAt = created });

        var reloaded = await store.GetByIdAsync("c1");

        Assert.Equal(DateTimeKind.Utc, reloaded!.CreatedAt.Kind);
        Assert.Equal(created, reloaded.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, reloaded.UpdatedAt.Kind);
    }
}
