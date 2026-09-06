using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

// r13 02-model-library.md 2.3/2.4: shared tune-profile upsert lifted out of ServicesViewModel,
// plus the pure staleness predicate used by Auto-tune all.
public sealed class LlamaTuneProfileStoreTests
{
    private static AppSettings NewAppSettings() => new();

    [Fact]
    public void Upsert_creates_a_new_profile_when_none_exists()
    {
        using var temp = new TempDir();
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");
        var settings = NewAppSettings();

        var profile = LlamaTuneProfileStore.Upsert(settings, modelPath, contextSize: 4096, extraArgs: "", currentGpuLayers: 20, currentThreads: 8);

        Assert.NotNull(profile);
        Assert.Single(settings.LlamaTuneProfiles);
        Assert.Equal(20, profile!.GpuLayers);
        Assert.Equal(GpuPlacementKind.Exact, profile.GpuPlacement?.Kind);
        Assert.Equal(8, profile.Threads);
        Assert.Equal(4096, profile.ContextSize);
    }

    [Fact]
    public void Upsert_updates_the_existing_profile_for_the_same_file_instead_of_duplicating()
    {
        using var temp = new TempDir();
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");
        var settings = NewAppSettings();

        LlamaTuneProfileStore.Upsert(settings, modelPath, 4096, "", 20, 8);
        var updated = LlamaTuneProfileStore.Upsert(settings, modelPath, 8192, "", 32, 16);

        Assert.Single(settings.LlamaTuneProfiles);
        Assert.Equal(32, updated!.GpuLayers);
        Assert.Equal(16, updated.Threads);
        Assert.Equal(8192, updated.ContextSize);
    }

    [Fact]
    public void Upsert_with_a_result_overrides_gpu_layers_threads_and_records_version()
    {
        using var temp = new TempDir();
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");
        var settings = NewAppSettings();
        var result = new ServerTuneResult(GpuLayers: 28, TotalLayers: 32, Threads: 12, LlamaServerVersion: "b5750", RecentLog: "log");

        var profile = LlamaTuneProfileStore.Upsert(settings, modelPath, 4096, "", currentGpuLayers: 0, currentThreads: 0, result: result);

        Assert.Equal(28, profile!.GpuLayers);
        Assert.Equal(32, profile.TotalLayers);
        Assert.Equal(12, profile.Threads);
        Assert.Equal("b5750", profile.LlamaServerVersion);
    }

    [Fact]
    public void Upsert_returns_null_and_changes_nothing_for_a_missing_file()
    {
        using var temp = new TempDir();
        var settings = NewAppSettings();

        var profile = LlamaTuneProfileStore.Upsert(settings, temp.PathFor("missing.gguf"), 4096, "", 1, 1);

        Assert.Null(profile);
        Assert.Empty(settings.LlamaTuneProfiles);
    }

    [Fact]
    public void Prune_removes_profiles_whose_file_no_longer_exists()
    {
        using var temp = new TempDir();
        var settings = NewAppSettings();
        settings.LlamaTuneProfiles.Add(new LlamaTuneProfile { ModelPath = temp.PathFor("gone.gguf"), TunedAtUtc = DateTime.UtcNow });

        LlamaTuneProfileStore.Prune(settings);

        Assert.Empty(settings.LlamaTuneProfiles);
    }

    [Fact]
    public void Prune_caps_at_MaxProfiles_keeping_the_most_recently_tuned()
    {
        using var temp = new TempDir();
        var settings = NewAppSettings();
        for (var i = 0; i < LlamaTuneProfileStore.MaxProfiles + 5; i++)
        {
            var path = temp.PathFor($"model-{i}.gguf");
            File.WriteAllText(path, "fake");
            settings.LlamaTuneProfiles.Add(new LlamaTuneProfile
            {
                ModelPath = path,
                ModelSizeBytes = new FileInfo(path).Length,
                ModelModifiedAtUtc = File.GetLastWriteTimeUtc(path),
                TunedAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        LlamaTuneProfileStore.Prune(settings);

        Assert.Equal(LlamaTuneProfileStore.MaxProfiles, settings.LlamaTuneProfiles.Count);
        Assert.Contains(settings.LlamaTuneProfiles, p => p.ModelPath.EndsWith("model-0.gguf", StringComparison.Ordinal));
        Assert.DoesNotContain(settings.LlamaTuneProfiles, p => p.ModelPath.EndsWith($"model-{LlamaTuneProfileStore.MaxProfiles + 4}.gguf", StringComparison.Ordinal));
    }

    [Fact]
    public void IsStale_returns_true_when_no_profile_exists()
    {
        Assert.True(LlamaTuneProfileStore.IsStale(null, 100, DateTime.UtcNow));
    }

    [Fact]
    public void IsStale_returns_false_for_a_fresh_matching_profile()
    {
        var mtime = DateTime.UtcNow;
        var profile = new LlamaTuneProfile { ModelSizeBytes = 100, ModelModifiedAtUtc = mtime, LlamaServerVersion = "b5750" };

        Assert.False(LlamaTuneProfileStore.IsStale(profile, 100, mtime, "b5750"));
    }

    [Fact]
    public void IsStale_returns_true_on_size_drift()
    {
        var mtime = DateTime.UtcNow;
        var profile = new LlamaTuneProfile { ModelSizeBytes = 100, ModelModifiedAtUtc = mtime };

        Assert.True(LlamaTuneProfileStore.IsStale(profile, 200, mtime));
    }

    [Fact]
    public void IsStale_returns_true_on_mtime_drift()
    {
        var profile = new LlamaTuneProfile { ModelSizeBytes = 100, ModelModifiedAtUtc = DateTime.UtcNow.AddDays(-1) };

        Assert.True(LlamaTuneProfileStore.IsStale(profile, 100, DateTime.UtcNow));
    }

    [Fact]
    public void IsStale_returns_true_on_llama_server_version_drift_when_both_are_known()
    {
        var mtime = DateTime.UtcNow;
        var profile = new LlamaTuneProfile { ModelSizeBytes = 100, ModelModifiedAtUtc = mtime, LlamaServerVersion = "b5750" };

        Assert.True(LlamaTuneProfileStore.IsStale(profile, 100, mtime, "b5900"));
    }

    [Fact]
    public void IsStale_ignores_version_when_either_side_is_unknown()
    {
        var mtime = DateTime.UtcNow;
        var profile = new LlamaTuneProfile { ModelSizeBytes = 100, ModelModifiedAtUtc = mtime, LlamaServerVersion = "" };

        Assert.False(LlamaTuneProfileStore.IsStale(profile, 100, mtime, "b5900"));
    }
}
