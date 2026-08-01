using Hermaeus.Core.Models;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r29 doc 02 2.1: the model tiles carry a badge saying where the model came
/// from. It is derived from a source kind rather than bound straight to RepoId,
/// so a second download provider is a new enum value instead of a new binding
/// in the template.
/// </summary>
public sealed class ModelSourceKindTests
{
    private static ModelProfileItemViewModel LocalGguf(string path = @"C:\models\model.gguf") =>
        new(new LlmModel { Id = path, Name = "model", Provider = "local GGUF" },
            new ModelProfile { ModelId = path });

    private static ModelProfileItemViewModel Remote() =>
        new(new LlmModel { Id = "gpt-4o", Name = "gpt-4o", Provider = "OpenAI" },
            new ModelProfile { ModelId = "gpt-4o" });

    [Fact]
    public void A_linked_repo_makes_the_source_hugging_face()
    {
        var item = LocalGguf();
        item.RepoId = "bartowski/Qwen2.5-7B-GGUF";

        Assert.Equal(ModelSourceKind.HuggingFace, item.SourceKind);
        Assert.Equal("Hugging Face", item.SourceLabel);
        Assert.True(item.HasKnownSource);
        Assert.Contains("bartowski/Qwen2.5-7B-GGUF", item.SourceTooltip);
    }

    [Fact]
    public void A_local_gguf_with_no_repo_is_a_local_file()
    {
        var item = LocalGguf();

        Assert.Equal(ModelSourceKind.LocalFile, item.SourceKind);
        Assert.Equal("Local file", item.SourceLabel);
        Assert.True(item.HasKnownSource);
    }

    [Fact]
    public void A_model_reported_by_a_running_provider_has_no_known_source()
    {
        var item = Remote();

        Assert.Equal(ModelSourceKind.Unknown, item.SourceKind);
        Assert.Equal(string.Empty, item.SourceLabel);
        Assert.False(item.HasKnownSource);
    }

    /// <summary>
    /// The one that breaks in practice: the badge is populated by an async
    /// manifest refresh (and by a repo link) after the row already exists, so
    /// the derived properties have to be told when RepoId changes.
    /// </summary>
    [Fact]
    public void Setting_the_repo_after_construction_raises_a_change_for_the_badge()
    {
        var item = LocalGguf();
        var raised = new List<string>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        item.RepoId = "bartowski/Qwen2.5-7B-GGUF";

        Assert.Contains(nameof(ModelProfileItemViewModel.SourceKind), raised);
        Assert.Contains(nameof(ModelProfileItemViewModel.SourceLabel), raised);
        Assert.Contains(nameof(ModelProfileItemViewModel.SourceBadgeText), raised);
        Assert.Contains(nameof(ModelProfileItemViewModel.SourceTooltip), raised);
        Assert.Contains(nameof(ModelProfileItemViewModel.HasKnownSource), raised);
    }

    [Fact]
    public void The_source_label_is_stable_text_not_a_model_written_string()
    {
        var hf = LocalGguf();
        hf.RepoId = "org/repo";

        Assert.Equal("Hugging Face", hf.SourceLabel);
        Assert.Equal("HF", hf.SourceBadgeText);
        Assert.Equal("Local file", LocalGguf().SourceLabel);
        Assert.Equal("Local", LocalGguf().SourceBadgeText);
    }

    /// <summary>
    /// doc 02 2.2: TuneSummary does not fit the tile, so it moves onto the Auto
    /// tune button's tooltip rather than being dropped.
    /// </summary>
    [Fact]
    public void The_tune_summary_moves_onto_the_auto_tune_tooltip_rather_than_being_dropped()
    {
        var item = LocalGguf();
        var withoutTune = item.AutoTuneTooltip;

        item.TuneSummary = "24/32 GPU layers, 8 threads";

        Assert.DoesNotContain("24/32", withoutTune);
        Assert.Contains("24/32 GPU layers, 8 threads", item.AutoTuneTooltip);
        Assert.Contains("Probes GPU layer counts", item.AutoTuneTooltip);
    }
}
