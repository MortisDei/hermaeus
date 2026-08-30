using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class EvidenceAndOutcomeTests
{
    [Theory]
    [InlineData(EvidenceOrigin.DirectObservation, "direct_observation")]
    [InlineData(EvidenceOrigin.DeterministicCalculation, "deterministic_calculation")]
    [InlineData(EvidenceOrigin.UserProvided, "user_provided")]
    [InlineData(EvidenceOrigin.Extracted, "extracted")]
    [InlineData(EvidenceOrigin.ModelInference, "model_inference")]
    public void Evidence_origins_write_explicit_distinct_values(EvidenceOrigin origin, string expected)
    {
        var json = JsonSerializer.Serialize(origin);
        Assert.Equal($"\"{expected}\"", json);
        Assert.Equal(origin, JsonSerializer.Deserialize<EvidenceOrigin>(json));
    }

    [Theory]
    [InlineData("0", EvidenceOrigin.DirectObservation)]
    [InlineData("1", EvidenceOrigin.UserProvided)]
    [InlineData("2", EvidenceOrigin.ModelInference)]
    [InlineData("\"Inferred\"", EvidenceOrigin.ModelInference)]
    [InlineData("\"inferred\"", EvidenceOrigin.ModelInference)]
    public void Evidence_origin_reads_legacy_representations(string json, EvidenceOrigin expected) =>
        Assert.Equal(expected, JsonSerializer.Deserialize<EvidenceOrigin>(json));

    [Fact]
    public void Evidence_origin_refuses_unknown_values()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EvidenceOrigin>("99"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EvidenceOrigin>("\"heuristic\""));
    }

    [Fact]
    public void Source_reference_round_trips_new_provenance_and_origin()
    {
        var source = new SourceReference(
            ProvenanceKind.RuntimeObservation,
            "Process memory sample",
            Locator: "sample-1",
            EvidenceOrigin: EvidenceOrigin.DeterministicCalculation);

        var reloaded = JsonSerializer.Deserialize<SourceReference>(JsonSerializer.Serialize(source));

        Assert.Equal(source, reloaded);
    }

    [Fact]
    public void Missing_agent_outcome_loads_as_legacy_unknown_without_summary_guessing()
    {
        const string json = """
            {"tool":"mcp:test:tool","result_summary":"SUCCESS: everything worked"}
            """;

        var result = JsonSerializer.Deserialize<AgentToolResult>(json, AgentJson.Options)!;

        Assert.Equal(NormalizedOutcome.Unknown, result.NormalizedOutcome.Outcome);
        Assert.Equal("legacy-no-normalized-outcome", result.NormalizedOutcome.EvidenceCode);
        Assert.Equal("SUCCESS: everything worked", result.ResultSummary);
    }

    [Theory]
    [InlineData(AgentToolOutcomeSignal.Completed, 0, NormalizedOutcome.Succeeded, "process-exit-zero")]
    [InlineData(AgentToolOutcomeSignal.Completed, 7, NormalizedOutcome.Failed, "process-exit-nonzero")]
    [InlineData(AgentToolOutcomeSignal.TimedOut, null, NormalizedOutcome.TimedOut, "process-timeout")]
    [InlineData(AgentToolOutcomeSignal.Cancelled, null, NormalizedOutcome.Cancelled, "caller-cancelled")]
    [InlineData(AgentToolOutcomeSignal.Unavailable, null, NormalizedOutcome.Unavailable, "process-executable-missing")]
    [InlineData(AgentToolOutcomeSignal.PolicyBlocked, null, NormalizedOutcome.Blocked, "command-validation-blocked")]
    public void Command_normalizer_uses_only_structured_process_evidence(
        object signal,
        int? exitCode,
        NormalizedOutcome expected,
        string code)
    {
        var result = AgentToolOutcomeNormalizer.Normalize("run_command", new AgentToolOutcomeEvidence((AgentToolOutcomeSignal)signal, exitCode, "bounded"));
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(code, result.EvidenceCode);
    }

    [Theory]
    [InlineData(AgentToolOutcomeSignal.Empty, NormalizedOutcome.NoEffect)]
    [InlineData(AgentToolOutcomeSignal.Completed, NormalizedOutcome.Succeeded)]
    [InlineData(AgentToolOutcomeSignal.PolicyBlocked, NormalizedOutcome.Blocked)]
    [InlineData(AgentToolOutcomeSignal.Unavailable, NormalizedOutcome.Unavailable)]
    [InlineData(AgentToolOutcomeSignal.Failed, NormalizedOutcome.Failed)]
    public void Workspace_collection_normalizer_preserves_semantic_distinctions(
        object signal,
        NormalizedOutcome expected)
    {
        var result = AgentToolOutcomeNormalizer.Normalize("search_files", new AgentToolOutcomeEvidence((AgentToolOutcomeSignal)signal, Detail: "bounded"));
        Assert.Equal(expected, result.Outcome);
    }

    [Theory]
    [InlineData(AgentToolOutcomeSignal.Completed, NormalizedOutcome.Succeeded)]
    [InlineData(AgentToolOutcomeSignal.NoEffect, NormalizedOutcome.NoEffect)]
    [InlineData(AgentToolOutcomeSignal.Partial, NormalizedOutcome.PartiallySucceeded)]
    [InlineData(AgentToolOutcomeSignal.PolicyBlocked, NormalizedOutcome.Blocked)]
    public void Mutation_normalizer_preserves_effect_and_guard_distinctions(
        object signal,
        NormalizedOutcome expected)
    {
        var result = AgentToolOutcomeNormalizer.Normalize("edit_file", new AgentToolOutcomeEvidence((AgentToolOutcomeSignal)signal, Detail: "bounded"));
        Assert.Equal(expected, result.Outcome);
    }

    [Theory]
    [InlineData(AgentToolOutcomeSignal.Completed, NormalizedOutcome.Succeeded)]
    [InlineData(AgentToolOutcomeSignal.UserDenied, NormalizedOutcome.Denied)]
    [InlineData(AgentToolOutcomeSignal.PolicyBlocked, NormalizedOutcome.Blocked)]
    public void Approval_normalizer_does_not_conflate_user_and_policy(
        object signal,
        NormalizedOutcome expected)
    {
        var result = AgentToolOutcomeNormalizer.Normalize("approval", new AgentToolOutcomeEvidence((AgentToolOutcomeSignal)signal, Detail: "bounded"));
        Assert.Equal(expected, result.Outcome);
    }

    [Theory]
    [InlineData(AgentToolOutcomeSignal.StructuredSuccess, NormalizedOutcome.Succeeded)]
    [InlineData(AgentToolOutcomeSignal.StructuredFailure, NormalizedOutcome.Failed)]
    [InlineData(AgentToolOutcomeSignal.Unclassified, NormalizedOutcome.Unknown)]
    public void Mcp_normalizer_requires_structured_completion_status(
        object signal,
        NormalizedOutcome expected)
    {
        var result = AgentToolOutcomeNormalizer.Normalize(
            "mcp:test:echo",
            new AgentToolOutcomeEvidence((AgentToolOutcomeSignal)signal, Detail: "SUCCESS: model-facing prose"));
        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public void Unregistered_tool_and_success_claim_remain_unknown()
    {
        var result = AgentToolOutcomeNormalizer.Normalize(
            "model_invented_tool",
            new AgentToolOutcomeEvidence(AgentToolOutcomeSignal.Completed, Detail: "Succeeded perfectly"));

        Assert.Equal(NormalizedOutcome.Unknown, result.Outcome);
        Assert.Equal("unregistered-outcome-normalizer", result.EvidenceCode);
    }

    [Fact]
    public void Proven_unavailable_unregistered_executor_is_not_unknown()
    {
        var result = AgentToolOutcomeNormalizer.Normalize(
            "model_invented_tool",
            new AgentToolOutcomeEvidence(AgentToolOutcomeSignal.Unavailable, Detail: "No executor is registered."));

        Assert.Equal(NormalizedOutcome.Unavailable, result.Outcome);
        Assert.Equal("tool-executor-unavailable", result.EvidenceCode);
    }

    [Fact]
    public void Agent_result_round_trip_preserves_raw_and_normalized_evidence()
    {
        var original = new AgentToolResult
        {
            Tool = "run_command",
            Arguments = new Dictionary<string, object?> { ["command"] = "dotnet test" },
            ResultSummary = "compiler output",
            ExitCode = 7,
            TimedOut = false,
            NormalizedOutcome = AgentToolOutcomeNormalizer.Normalize(
                "run_command",
                new AgentToolOutcomeEvidence(AgentToolOutcomeSignal.Completed, 7, "Process exited with code 7."))
        };

        var json = JsonSerializer.Serialize(original, AgentJson.Options);
        var reloaded = JsonSerializer.Deserialize<AgentToolResult>(json, AgentJson.Options)!;

        Assert.Equal("compiler output", reloaded.ResultSummary);
        Assert.Equal(7, reloaded.ExitCode);
        Assert.False(reloaded.TimedOut);
        Assert.Equal(NormalizedOutcome.Failed, reloaded.NormalizedOutcome.Outcome);
        Assert.Contains("normalized_outcome", json, StringComparison.Ordinal);
        Assert.Contains("result_summary", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Transcript_carries_normalized_outcome_additively()
    {
        var result = new AgentToolResult
        {
            Tool = "read_file",
            ResultSummary = "contents",
            Source = new SourceReference(ProvenanceKind.Workspace, "README.md"),
            NormalizedOutcome = AgentToolOutcomeNormalizer.Normalize(
                "read_file", new AgentToolOutcomeEvidence(AgentToolOutcomeSignal.Completed, Detail: "Read completed."))
        };
        var entry = AgentTranscriptCompactor.FromToolResult(2, result, DateTime.UtcNow);

        var reloaded = JsonSerializer.Deserialize<AgentTranscriptEntry>(
            JsonSerializer.Serialize(entry, AgentJson.CompactOptions), AgentJson.CompactOptions)!;

        Assert.Equal(NormalizedOutcome.Succeeded, reloaded.NormalizedOutcome?.Outcome);
        Assert.True(reloaded.ReplaySafe);
    }

    [Fact]
    public async Task Executor_marks_valid_empty_collection_as_no_effect()
    {
        var root = Directory.CreateTempSubdirectory("hermaeus-outcome-empty-");
        try
        {
            var executor = new AgentToolExecutor(new AgentWorkspaceTools());
            var result = await executor.ExecuteAsync("list_files", [], new AgentWorkspaceOptions(root.FullName));

            Assert.Equal(NormalizedOutcome.NoEffect, result.NormalizedOutcome.Outcome);
            Assert.Empty(JsonSerializer.Deserialize<string[]>(result.ResultSummary) ?? []);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Executor_marks_missing_read_target_as_unavailable()
    {
        var root = Directory.CreateTempSubdirectory("hermaeus-outcome-missing-");
        try
        {
            var executor = new AgentToolExecutor(new AgentWorkspaceTools());
            var result = await executor.ExecuteAsync("read_file",
                new Dictionary<string, object?> { ["path"] = "missing.md" },
                new AgentWorkspaceOptions(root.FullName));

            Assert.Equal(NormalizedOutcome.Unavailable, result.NormalizedOutcome.Outcome);
            Assert.Contains("does not exist", result.ResultSummary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Executor_marks_unregistered_tool_as_unavailable()
    {
        var root = Directory.CreateTempSubdirectory("hermaeus-outcome-unregistered-");
        try
        {
            var executor = new AgentToolExecutor(new AgentWorkspaceTools());
            var result = await executor.ExecuteAsync(
                "model_invented_tool", [], new AgentWorkspaceOptions(root.FullName));

            Assert.Equal(NormalizedOutcome.Unavailable, result.NormalizedOutcome.Outcome);
            Assert.Equal("tool-executor-unavailable", result.NormalizedOutcome.EvidenceCode);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Executor_marks_verified_identical_edit_as_no_effect()
    {
        var root = Directory.CreateTempSubdirectory("hermaeus-outcome-edit-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "note.txt"), "same");
            var executor = new AgentToolExecutor(new AgentWorkspaceTools());
            var result = await executor.ExecuteAsync("edit_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "note.txt",
                    ["old_string"] = "same",
                    ["new_string"] = "same"
                },
                new AgentWorkspaceOptions(root.FullName));

            Assert.Equal(NormalizedOutcome.NoEffect, result.NormalizedOutcome.Outcome);
            Assert.Equal("same", await File.ReadAllTextAsync(Path.Combine(root.FullName, "note.txt")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false, NormalizedOutcome.Succeeded)]
    [InlineData(true, NormalizedOutcome.Failed)]
    [InlineData(null, NormalizedOutcome.Unknown)]
    public async Task Executor_uses_mcp_status_not_response_text(bool? isError, NormalizedOutcome expected)
    {
        var root = Directory.CreateTempSubdirectory("hermaeus-outcome-mcp-");
        try
        {
            var executor = new AgentToolExecutor(
                new AgentWorkspaceTools(),
                new StubMcpBridge(new McpToolExecutionResult("SUCCESS: untrusted response text", isError)));

            var result = await executor.ExecuteAsync(
                "mcp:test:echo", [], new AgentWorkspaceOptions(root.FullName));

            Assert.Equal(expected, result.NormalizedOutcome.Outcome);
            Assert.Equal("\"SUCCESS: untrusted response text\"", result.ResultSummary);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Safety_authority_types_do_not_accept_normalized_outcome_inputs()
    {
        var forbidden = typeof(NormalizedToolOutcome);
        var authorityTypes = new[] { typeof(AgentSafetyGate), typeof(WorkspacePolicyEvaluator), typeof(AgentApprovalFingerprint) };

        Assert.All(authorityTypes, type =>
        {
            Assert.DoesNotContain(type.GetConstructors(), constructor =>
                constructor.GetParameters().Any(parameter => parameter.ParameterType == forbidden));
            Assert.DoesNotContain(type.GetMethods(), method =>
                method.GetParameters().Any(parameter => parameter.ParameterType == forbidden));
        });
    }

    private sealed class StubMcpBridge(McpToolExecutionResult result) : IMcpToolBridge
    {
        public bool CanExecute(string toolName) => true;

        public Task<McpToolExecutionResult> ExecuteAsync(
            string toolName,
            Dictionary<string, object?> arguments,
            CancellationToken ct = default) => Task.FromResult(result);
    }
}
