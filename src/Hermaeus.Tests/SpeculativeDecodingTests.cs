using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r27 03-drafting-and-proof.md. Draft-model speculative decoding was deferred
/// from r18 4.4 because it needed a second model file, a second VRAM budget,
/// and a picker whose wrong answer costs performance silently. MTP heads ship
/// beside their base model and share its vocabulary by construction, which
/// removes the first two; 3.3 handles the third.
/// </summary>
public sealed class SpeculativeDecodingTests
{
    private static List<string> Args(ServerConfig cfg) => ServerProcessManager.BuildLaunchArguments(cfg).ToList();

    private static string? ArgValue(List<string> args, string flag)
    {
        var i = args.IndexOf(flag);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }

    // ── 3.1 The settings migration ──────────────────────────────────────────

    [Fact]
    public void A_legacy_ngram_bool_upgrades_to_a_types_list_exactly_once()
    {
        var server = new ServerConfig { NgramSpeculative = true };

        SettingsService.UpgradeSpeculativeDecoding(server);

        Assert.Equal(["ngram-mod"], server.Speculative.Types);
        Assert.False(server.NgramSpeculative, "the legacy property is read once and never written again");

        // Running the upgrade again must not append a second copy.
        SettingsService.UpgradeSpeculativeDecoding(server);
        Assert.Equal(["ngram-mod"], server.Speculative.Types);
    }

    [Fact]
    public void An_already_upgraded_config_is_not_re_upgraded_or_duplicated()
    {
        var server = new ServerConfig
        {
            NgramSpeculative = true,
            Speculative = new SpeculativeDecodingConfig { Types = ["draft-mtp"] }
        };

        SettingsService.UpgradeSpeculativeDecoding(server);

        // A user who has since chosen drafting must not have ngram-mod
        // resurrected on top of it by a stale legacy flag.
        Assert.Equal(["draft-mtp"], server.Speculative.Types);
    }

    [Fact]
    public void A_0_33_settings_file_produces_byte_identical_launch_arguments_after_upgrade()
    {
        // What 0.33.0 wrote and what its ServerProcessManager emitted for it.
        var legacy = new ServerConfig { ModelPath = "model.gguf", NgramSpeculative = true };
        var before = new List<string>(Args(new ServerConfig { ModelPath = "model.gguf" }))
        {
            "--spec-type", "ngram-mod"
        };

        SettingsService.UpgradeSpeculativeDecoding(legacy);

        Assert.Equal(before, Args(legacy));
    }

    [Fact]
    public void A_config_that_never_touched_speculative_decoding_launches_unchanged()
    {
        var untouched = new ServerConfig { ModelPath = "model.gguf" };
        Assert.DoesNotContain("--spec-type", Args(untouched));
    }

    // ── 3.2 The flags, as the installed binary names them ───────────────────

    [Fact]
    public void Two_types_emit_one_comma_separated_spec_type()
    {
        var args = Args(new ServerConfig
        {
            ModelPath = "model.gguf",
            Speculative = new SpeculativeDecodingConfig { Types = ["ngram-mod", "draft-mtp"], DraftModelPath = "mtp.gguf" }
        });

        Assert.Equal(1, args.Count(a => a == "--spec-type"));
        Assert.Equal("ngram-mod,draft-mtp", ArgValue(args, "--spec-type"));
    }

    [Fact]
    public void Empty_types_emit_no_spec_type_at_all()
    {
        var args = Args(new ServerConfig
        {
            ModelPath = "model.gguf",
            Speculative = new SpeculativeDecodingConfig { Types = [], DraftModelPath = "mtp.gguf", NMax = 5 }
        });

        Assert.DoesNotContain("--spec-type", args);
        Assert.DoesNotContain("--spec-draft-model", args);
        Assert.DoesNotContain("--spec-draft-n-max", args);
    }

    /// <summary>
    /// The single most likely way this document gets implemented wrongly: an
    /// agent writing from prior knowledge of llama.cpp emits --draft-max, the
    /// server starts fine, nothing changes, and the feature appears to work.
    /// These flags were verified as removed against the installed b10195
    /// binary, which prints "the argument has been removed" for each.
    /// </summary>
    [Fact]
    public void No_removed_flag_name_is_ever_emitted()
    {
        var args = Args(new ServerConfig
        {
            ModelPath = "model.gguf",
            Speculative = new SpeculativeDecodingConfig
            {
                Types = ["draft-mtp", "ngram-mod"],
                DraftModelPath = "mtp.gguf",
                DraftGpuLayers = 99,
                NMax = 5,
                NMin = 1,
                PMin = 0.25
            }
        });

        foreach (var removed in new[]
                 {
                     "--draft", "--draft-n", "--draft-max", "--draft-min", "--draft-n-min",
                     "--spec-ngram-size-n", "--spec-ngram-size-m", "--spec-ngram-min-hits"
                 })
        {
            Assert.DoesNotContain(removed, args);
        }

        // And the flags that do exist are emitted under the names the binary lists.
        Assert.Equal("mtp.gguf", ArgValue(args, "--spec-draft-model"));
        Assert.Equal("99", ArgValue(args, "-ngld"));
        Assert.Equal("5", ArgValue(args, "--spec-draft-n-max"));
        Assert.Equal("1", ArgValue(args, "--spec-draft-n-min"));
        Assert.Equal("0.25", ArgValue(args, "--spec-draft-p-min"));
    }

    [Fact]
    public void ExtraArgs_containing_spec_type_still_suppresses_the_generated_one()
    {
        var args = Args(new ServerConfig
        {
            ModelPath = "model.gguf",
            ExtraArgs = "--spec-type ngram-cache",
            Speculative = new SpeculativeDecodingConfig { Types = ["draft-mtp"], DraftModelPath = "mtp.gguf" }
        });

        // The r18 escape hatch is unchanged: ExtraArgs always wins.
        Assert.Equal(1, args.Count(a => a == "--spec-type"));
        Assert.Equal("ngram-cache", ArgValue(args, "--spec-type"));
        Assert.DoesNotContain("--spec-draft-model", args);
    }

    [Fact]
    public void Draft_settings_emit_nothing_when_no_draft_type_is_selected()
    {
        var args = Args(new ServerConfig
        {
            ModelPath = "model.gguf",
            Speculative = new SpeculativeDecodingConfig
            {
                Types = ["ngram-mod"],
                DraftModelPath = "mtp.gguf",
                DraftGpuLayers = 99,
                NMax = 4
            }
        });

        Assert.Equal("ngram-mod", ArgValue(args, "--spec-type"));
        Assert.DoesNotContain("--spec-draft-model", args);
        Assert.DoesNotContain("-ngld", args);
        // n-max is not draft-specific: ngram-mod drafts too, so it still applies.
        Assert.Equal("4", ArgValue(args, "--spec-draft-n-max"));
    }

    [Fact]
    public void A_p_min_is_written_with_an_invariant_decimal_point()
    {
        var args = Args(new ServerConfig
        {
            ModelPath = "model.gguf",
            Speculative = new SpeculativeDecodingConfig { Types = ["draft-mtp"], DraftModelPath = "mtp.gguf", PMin = 0.4 }
        });

        // A comma decimal separator would be a different number to the server.
        Assert.Equal("0.4", ArgValue(args, "--spec-draft-p-min"));
    }

    // ── 3.3 An incompatible draft is refused before launch ──────────────────

    [Fact]
    public void A_draft_path_that_does_not_exist_refuses_the_start_and_names_the_path()
    {
        var result = SpeculativeDecodingValidator.Validate(new ServerConfig
        {
            ModelPath = "model.gguf",
            Speculative = new SpeculativeDecodingConfig { Types = ["draft-mtp"], DraftModelPath = "nowhere/absent-draft.gguf" }
        });

        Assert.True(result.IsRefusal);
        Assert.Contains("absent-draft.gguf", result.Message);
    }

    [Fact]
    public void A_draft_type_with_no_draft_path_refuses_and_says_why()
    {
        var result = SpeculativeDecodingValidator.Validate(new ServerConfig
        {
            ModelPath = "model.gguf",
            Speculative = new SpeculativeDecodingConfig { Types = ["draft-mtp"] }
        });

        Assert.True(result.IsRefusal);
        Assert.Contains("draft-mtp", result.Message);
    }

    [Fact]
    public void A_draft_path_containing_traversal_segments_is_rejected()
    {
        var result = SpeculativeDecodingValidator.Validate(new ServerConfig
        {
            ModelPath = "model.gguf",
            Speculative = new SpeculativeDecodingConfig { Types = ["draft-mtp"], DraftModelPath = "models/../../etc/passwd.gguf" }
        });

        Assert.True(result.IsRefusal);
        Assert.Contains("..", result.Message);
    }

    [Fact]
    public void An_ngram_only_config_needs_no_draft_model_and_validates_clean()
    {
        var result = SpeculativeDecodingValidator.Validate(new ServerConfig
        {
            ModelPath = "model.gguf",
            Speculative = new SpeculativeDecodingConfig { Types = ["ngram-mod"] }
        });

        Assert.Equal(SpeculativeValidationSeverity.Ok, result.Severity);
    }

    [Fact]
    public void A_vocabulary_size_mismatch_refuses_and_names_both_sizes()
    {
        using var temp = new TempDir();
        var target = WriteGgufWithVocabulary(temp, "target.gguf", vocabularySize: 262144, padBytes: 4096);
        var draft = WriteGgufWithVocabulary(temp, "draft.gguf", vocabularySize: 32000, padBytes: 64);

        var result = SpeculativeDecodingValidator.Validate(new ServerConfig
        {
            ModelPath = target,
            Speculative = new SpeculativeDecodingConfig { Types = ["draft-mtp"], DraftModelPath = draft }
        });

        Assert.True(result.IsRefusal);
        Assert.Contains("262,144", result.Message);
        Assert.Contains("32,000", result.Message);
        Assert.Contains("target.gguf", result.Message);
        Assert.Contains("draft.gguf", result.Message);
    }

    [Fact]
    public void A_matching_vocabulary_and_a_small_draft_validates_clean()
    {
        using var temp = new TempDir();
        var target = WriteGgufWithVocabulary(temp, "target.gguf", vocabularySize: 262144, padBytes: 400_000);
        var draft = WriteGgufWithVocabulary(temp, "mtp.gguf", vocabularySize: 262144, padBytes: 1024);

        var result = SpeculativeDecodingValidator.Validate(new ServerConfig
        {
            ModelPath = target,
            Speculative = new SpeculativeDecodingConfig { Types = ["draft-mtp"], DraftModelPath = draft }
        });

        Assert.Equal(SpeculativeValidationSeverity.Ok, result.Severity);
    }

    [Fact]
    public void A_draft_larger_than_half_the_target_warns_but_does_not_refuse()
    {
        using var temp = new TempDir();
        var target = WriteGgufWithVocabulary(temp, "target.gguf", vocabularySize: 262144, padBytes: 100_000);
        var draft = WriteGgufWithVocabulary(temp, "big-draft.gguf", vocabularySize: 262144, padBytes: 90_000);

        var result = SpeculativeDecodingValidator.Validate(new ServerConfig
        {
            ModelPath = target,
            Speculative = new SpeculativeDecodingConfig { Types = ["draft-mtp"], DraftModelPath = draft }
        });

        // A bad idea rather than a broken one; the speed check will show it.
        Assert.Equal(SpeculativeValidationSeverity.Warning, result.Severity);
        Assert.False(result.IsRefusal);
    }

    // ── 3.5 The speed check ─────────────────────────────────────────────────

    [Fact]
    public void The_speed_check_suite_is_well_formed_and_asserts_nothing_about_quality()
    {
        var suite = SpeedCheck.Suite();

        Assert.Equal(SpeedCheck.SuiteId, suite.Id);
        Assert.NotEmpty(suite.Cases);
        Assert.Equal(suite.Cases.Count, suite.Cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (var c in suite.Cases)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Prompt), $"'{c.Name}' needs a prompt");
            // It is a speed measurement. A quality assertion here would turn a
            // throughput number into a pass/fail nobody asked for.
            Assert.Empty(c.ExpectedKeywords);
            Assert.Empty(c.ExpectedRegexes);
            Assert.False(c.ShouldRefuse);
        }

        // The shapes where drafting behaves differently must both be present.
        Assert.Contains(suite.Cases, c => c.Name.Contains("Structured", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(suite.Cases, c => c.Name.Contains("prose", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_speed_check_suite_ships_alongside_the_starter_suites()
        => Assert.Contains(BenchmarkService.StarterSuites(), s => s.Id == SpeedCheck.SuiteId);

    [Fact]
    public void Run_metadata_round_trips_the_speculative_settings()
    {
        var metadata = new BenchmarkRunMetadata
        {
            SpeculativeTypes = "draft-mtp",
            SpeculativeDraftModel = "mtp-gemma-4-E4B-it.gguf",
            SpeculativeNMax = 5,
            SpeculativeNMin = 1,
            SpeculativePMin = 0.25,
            SpeculativeDraftGpuLayers = 99
        };

        var json = System.Text.Json.JsonSerializer.Serialize(metadata);
        var back = System.Text.Json.JsonSerializer.Deserialize<BenchmarkRunMetadata>(json)!;

        Assert.Equal("draft-mtp", back.SpeculativeTypes);
        Assert.Equal("mtp-gemma-4-E4B-it.gguf", back.SpeculativeDraftModel);
        Assert.Equal(5, back.SpeculativeNMax);
        Assert.Equal(1, back.SpeculativeNMin);
        Assert.Equal(0.25, back.SpeculativePMin);
        Assert.Equal(99, back.SpeculativeDraftGpuLayers);
        Assert.Contains("mtp-gemma-4-E4B-it.gguf", back.SpeculativeSummary);
        Assert.Equal("speculative decoding off", new BenchmarkRunMetadata().SpeculativeSummary);
    }

    // ── 3.6 The comparison ──────────────────────────────────────────────────

    private static BenchmarkRun Run(string id, string modelId, string suiteId, string types, double tps, double firstToken) => new()
    {
        Id = id,
        ModelId = modelId,
        ModelName = modelId,
        SuiteId = suiteId,
        SuiteName = suiteId,
        StartedAt = DateTime.UtcNow,
        Metadata = new BenchmarkRunMetadata { SpeculativeTypes = types },
        Results =
        [
            new BenchmarkResult { ApproxTokensPerSecond = tps, FirstTokenMs = (long)firstToken, PromptTokensPerSecond = 500 }
        ]
    };

    [Fact]
    public void Comparison_pairs_two_runs_by_model_and_suite_and_reports_the_config_delta()
    {
        var baseline = Run("a", "gemma.gguf", SpeedCheck.SuiteId, "ngram-mod", tps: 30, firstToken: 400);
        var candidate = Run("b", "gemma.gguf", SpeedCheck.SuiteId, "draft-mtp", tps: 44, firstToken: 380);

        var result = SpeedCheckComparer.Compare(baseline, candidate);

        Assert.True(result.Compared);
        var comparison = result.Comparison!;
        Assert.Equal(14, comparison.TokensPerSecondDelta, 3);
        Assert.Equal(-20, comparison.FirstTokenMsDelta, 3);
        Assert.Equal("ngram-mod -> draft-mtp", comparison.ConfigurationDelta);
    }

    [Fact]
    public void Comparison_refuses_runs_of_different_models_or_suites()
    {
        var baseline = Run("a", "gemma.gguf", SpeedCheck.SuiteId, "ngram-mod", 30, 400);

        var differentModel = SpeedCheckComparer.Compare(baseline, Run("b", "qwen.gguf", SpeedCheck.SuiteId, "draft-mtp", 44, 380));
        Assert.False(differentModel.Compared);
        Assert.Contains("different models", differentModel.Refusal);

        var differentSuite = SpeedCheckComparer.Compare(baseline, Run("c", "gemma.gguf", "speed-smoke", "draft-mtp", 44, 380));
        Assert.False(differentSuite.Compared);
        Assert.Contains("different suites", differentSuite.Refusal);

        Assert.False(SpeedCheckComparer.Compare(baseline, baseline).Compared);
        Assert.False(SpeedCheckComparer.Compare(baseline, null).Compared);
    }

    [Fact]
    public void Comparison_reports_no_verdict_grade_or_recommendation()
    {
        // Settled by r23 2.3 and unchanged: the app reports what happened, it
        // does not rate itself. A property here would be the crack that a grade
        // grows through later.
        var names = typeof(SpeedCheckComparison).GetProperties().Select(p => p.Name).ToList();
        foreach (var forbidden in new[] { "Verdict", "Grade", "Score", "Recommendation", "Confidence", "Winner" })
            Assert.DoesNotContain(forbidden, names);
    }

    // ── 3.7 Doctor ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseTypes_accepts_the_separators_a_user_will_actually_type()
    {
        Assert.Equal(["ngram-mod", "draft-mtp"], ServerProcessViewModel.ParseTypes("ngram-mod, draft-mtp"));
        Assert.Equal(["ngram-mod", "draft-mtp"], ServerProcessViewModel.ParseTypes("ngram-mod draft-mtp"));
        Assert.Equal(["ngram-mod"], ServerProcessViewModel.ParseTypes("ngram-mod, ngram-mod"));
        Assert.Empty(ServerProcessViewModel.ParseTypes("   "));
        Assert.Empty(ServerProcessViewModel.ParseTypes(null));
    }

    // ── Fixtures ────────────────────────────────────────────────────────────

    /// <summary>
    /// A minimal but structurally valid GGUF header declaring one architecture
    /// and one vocab_size key, padded to a chosen file size so the draft-size
    /// ratio check has something real to measure.
    /// </summary>
    private static string WriteGgufWithVocabulary(TempDir temp, string name, int vocabularySize, int padBytes)
    {
        var path = temp.PathFor(name);
        using var stream = File.Create(path);
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
            w.Write((uint)3);
            w.Write((ulong)0);  // tensor_count
            w.Write((ulong)2);  // kv_count

            WriteKey(w, "general.architecture");
            w.Write((uint)8);   // string
            WriteString(w, "gemma3");

            WriteKey(w, "gemma3.vocab_size");
            w.Write((uint)4);   // uint32
            w.Write((uint)vocabularySize);
        }

        if (padBytes > 0)
            stream.Write(new byte[padBytes]);
        return path;
    }

    private static void WriteKey(BinaryWriter w, string key) => WriteString(w, key);

    private static void WriteString(BinaryWriter w, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        w.Write((ulong)bytes.Length);
        w.Write(bytes);
    }
}
