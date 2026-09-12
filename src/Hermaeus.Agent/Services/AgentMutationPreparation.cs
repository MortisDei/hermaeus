using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Agent.Services;

public sealed record AgentMutationPreparationResult(
    AgentPendingToolAction? Pending,
    string Error)
{
    public bool IsValid => Pending is not null && Error.Length == 0;
    public string? Content { get; init; }
    public bool Existed { get; init; }

    public static AgentMutationPreparationResult Reject(string error) => new(null, error);
}

/// <summary>
/// Builds an approvable mutation only after its arguments, target, policy,
/// capability, pre-image and complete proposed output have been checked. This
/// class never writes the workspace.
/// </summary>
public static class AgentMutationPreparation
{
    private const int SchemaVersion = 1;

    public static async Task<AgentMutationPreparationResult> PrepareAsync(
        string toolName,
        Dictionary<string, object?> arguments,
        AgentWorkspaceOptions options,
        WorkspacePolicy? policy,
        IAgentWorkspaceTools? workspaceTools,
        CancellationToken ct = default)
    {
        if (arguments is null)
            return AgentMutationPreparationResult.Reject("The tool arguments object is required.");

        var normalizedTool = toolName.Trim().ToLowerInvariant();
        return normalizedTool switch
        {
            "edit_file" => await PrepareEditAsync(arguments, options, policy, workspaceTools, ct),
            "create_file" => await PrepareCreateAsync(arguments, options, policy, workspaceTools, ct),
            "apply_draft_patch" => await PrepareReplacementAsync(arguments, options, policy, workspaceTools, ct),
            "run_command" => PrepareCommand(arguments, options, policy),
            _ => AgentMutationPreparationResult.Reject($"Tool '{toolName}' is not a prepared mutation.")
        };
    }

    public static Dictionary<string, object?> CloneArguments(IReadOnlyDictionary<string, object?> arguments)
    {
        var clone = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in arguments)
        {
            clone[pair.Key] = pair.Value is JsonElement element ? element.Clone() : pair.Value;
        }

        return clone;
    }

    public static string ComputeContentSha256(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    public static string ComputePolicyFingerprint(WorkspacePolicy? policy)
    {
        var payload = JsonSerializer.Serialize(policy, AgentJson.CompactOptions);
        return ComputeContentSha256(payload);
    }

    public static string NormalizeReplacementContent(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static async Task<AgentMutationPreparationResult> PrepareEditAsync(
        Dictionary<string, object?> arguments,
        AgentWorkspaceOptions options,
        WorkspacePolicy? policy,
        IAgentWorkspaceTools? workspaceTools,
        CancellationToken ct)
    {
        var schema = ValidateKeys(arguments, ["relative_path", "old_string", "new_string"]);
        if (schema is not null) return AgentMutationPreparationResult.Reject(schema);
        if (!TryGetRequiredString(arguments, "relative_path", allowEmpty: false, out var relativePath, out var error))
            return AgentMutationPreparationResult.Reject(error);
        if (!TryGetRequiredString(arguments, "old_string", allowEmpty: false, out var oldString, out error))
            return AgentMutationPreparationResult.Reject(error);
        if (!TryGetRequiredString(arguments, "new_string", allowEmpty: true, out var newString, out error))
            return AgentMutationPreparationResult.Reject(error);

        var rootResult = ResolveRoot(options, out var root);
        if (rootResult is not null) return AgentMutationPreparationResult.Reject(rootResult);
        var target = await PrepareTargetAsync(root!, relativePath, options, policy, workspaceTools, mustExist: true, ct);
        if (!target.IsValid) return target;

        var current = target.Content!;
        var count = CountOccurrences(current, oldString);
        if (count == 0)
            return AgentMutationPreparationResult.Reject("edit_file's old_string was not found in the target file; re-read it before proposing the edit.");
        if (count > 1)
            return AgentMutationPreparationResult.Reject($"edit_file's old_string matched {count} times; it must match exactly once.");

        var index = current.IndexOf(oldString, StringComparison.Ordinal);
        var proposed = string.Concat(current.AsSpan(0, index), newString, current.AsSpan(index + oldString.Length));
        return BuildFilePending(
            "edit_file", AgentMutationKind.Edit, arguments, target, proposed, policy, options.MaxFileBytes);
    }

    private static async Task<AgentMutationPreparationResult> PrepareCreateAsync(
        Dictionary<string, object?> arguments,
        AgentWorkspaceOptions options,
        WorkspacePolicy? policy,
        IAgentWorkspaceTools? workspaceTools,
        CancellationToken ct)
    {
        var schema = ValidateKeys(arguments, ["relative_path", "content"]);
        if (schema is not null) return AgentMutationPreparationResult.Reject(schema);
        if (!TryGetRequiredString(arguments, "relative_path", allowEmpty: false, out var relativePath, out var error))
            return AgentMutationPreparationResult.Reject(error);
        if (!TryGetRequiredString(arguments, "content", allowEmpty: true, out var content, out error))
            return AgentMutationPreparationResult.Reject(error);

        var rootResult = ResolveRoot(options, out var root);
        if (rootResult is not null) return AgentMutationPreparationResult.Reject(rootResult);
        var target = await PrepareTargetAsync(root!, relativePath, options, policy, workspaceTools, mustExist: false, ct);
        if (!target.IsValid) return target;
        if (target.Existed)
            return AgentMutationPreparationResult.Reject("create_file refuses to overwrite an existing file; use edit_file instead.");

        return BuildFilePending("create_file", AgentMutationKind.Create, arguments, target, content, policy, options.MaxFileBytes);
    }

    private static async Task<AgentMutationPreparationResult> PrepareReplacementAsync(
        Dictionary<string, object?> arguments,
        AgentWorkspaceOptions options,
        WorkspacePolicy? policy,
        IAgentWorkspaceTools? workspaceTools,
        CancellationToken ct)
    {
        var schema = ValidateKeys(arguments, ["relative_path", "proposed_content"]);
        if (schema is not null) return AgentMutationPreparationResult.Reject(schema);
        if (!TryGetRequiredString(arguments, "relative_path", allowEmpty: false, out var relativePath, out var error))
            return AgentMutationPreparationResult.Reject(error);
        if (!TryGetRequiredString(arguments, "proposed_content", allowEmpty: true, out var content, out error))
            return AgentMutationPreparationResult.Reject(error);

        var rootResult = ResolveRoot(options, out var root);
        if (rootResult is not null) return AgentMutationPreparationResult.Reject(rootResult);
        var target = await PrepareTargetAsync(root!, relativePath, options, policy, workspaceTools, mustExist: false, ct);
        if (!target.IsValid) return target;

        return BuildFilePending(
            "apply_draft_patch", AgentMutationKind.ApplyDraftPatch, arguments, target,
            NormalizeReplacementContent(content), policy, options.MaxFileBytes);
    }

    private static AgentMutationPreparationResult PrepareCommand(
        Dictionary<string, object?> arguments,
        AgentWorkspaceOptions options,
        WorkspacePolicy? policy)
    {
        var schema = ValidateKeys(arguments, ["command"]);
        if (schema is not null) return AgentMutationPreparationResult.Reject(schema);
        if (!TryGetRequiredString(arguments, "command", allowEmpty: false, out var command, out var error))
            return AgentMutationPreparationResult.Reject(error);

        var rootResult = ResolveRoot(options, out var root);
        if (rootResult is not null) return AgentMutationPreparationResult.Reject(rootResult);
        if (WorkspaceCommandRecipes.TryMatch(command, root!) is null)
            return AgentMutationPreparationResult.Reject("The requested command is not a valid fixed recipe or its argument failed workspace validation.");

        var preparedArguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["command"] = command
        };
        var pending = BuildPending("run_command", AgentMutationKind.Command, preparedArguments, root!, string.Empty,
            expectedPreImage: null, expectedPreImageExisted: false, policy);
        return new AgentMutationPreparationResult(pending, string.Empty);
    }

    private static async Task<AgentMutationPreparationResult> PrepareTargetAsync(
        string root,
        string relativePath,
        AgentWorkspaceOptions options,
        WorkspacePolicy? policy,
        IAgentWorkspaceTools? workspaceTools,
        bool mustExist,
        CancellationToken ct)
    {
        string full;
        try
        {
            full = AgentWorkspaceTools.ResolveSafePath(root, relativePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return AgentMutationPreparationResult.Reject($"The mutation target was refused: {ex.Message}");
        }

        var normalized = Path.GetRelativePath(root, full).Replace('\\', '/');
        var writeVerdict = WorkspacePolicyEvaluator.EvaluateWrite(policy, normalized);
        if (!writeVerdict.Allowed)
            return AgentMutationPreparationResult.Reject($"write blocked by workspace policy: {writeVerdict.Reason}");
        var readVerdict = WorkspacePolicyEvaluator.EvaluateRead(policy, normalized);
        if (!readVerdict.Allowed)
            return AgentMutationPreparationResult.Reject($"read blocked by workspace policy: {readVerdict.Reason}");
        if (!SupportedTextFileTypes.IsSupported(normalized))
            return AgentMutationPreparationResult.Reject("The mutation target is not a supported text file type.");
        if (Directory.Exists(full))
            return AgentMutationPreparationResult.Reject("The mutation target is a directory, not a file.");

        var existed = File.Exists(full);
        if (mustExist && !existed)
            return AgentMutationPreparationResult.Reject("The mutation target does not exist; use create_file for a new file.");

        string? content = null;
        if (existed)
        {
            var info = new FileInfo(full);
            if (!AgentWorkspaceTools.IsSafeTextFile(info, EffectiveMaxFileBytes(options.MaxFileBytes)))
                return AgentMutationPreparationResult.Reject("The existing target is ignored, too large, binary, or not a supported text file.");
            content = await File.ReadAllTextAsync(full, ct);
            if (content.Contains('\0'))
                return AgentMutationPreparationResult.Reject("The existing target appears to be binary.");
        }

        return new AgentMutationPreparationResult(
            new AgentPendingToolAction
            {
                WorkspaceRoot = root,
                RelativePath = normalized,
                ExpectedPreImageExisted = existed,
                ExpectedPreImageSha256 = existed ? ComputeContentSha256(content!) : string.Empty,
                ProposedContent = string.Empty,
                PreparedAt = DateTime.UtcNow,
                SchemaVersion = SchemaVersion
            },
            string.Empty)
        {
            Content = content,
            Existed = existed
        };
    }

    private static AgentMutationPreparationResult BuildFilePending(
        string toolName,
        AgentMutationKind kind,
        Dictionary<string, object?> arguments,
        AgentMutationPreparationResult targetResult,
        string proposedContent,
        WorkspacePolicy? policy,
        int maxFileBytes)
    {
        var target = targetResult.Pending!;
        var maxBytes = EffectiveMaxFileBytes(maxFileBytes);
        if (Encoding.UTF8.GetByteCount(proposedContent) > maxBytes)
            return AgentMutationPreparationResult.Reject($"The proposed output exceeds the {maxBytes / 1024} KB workspace mutation limit.");

        var pendingArguments = CloneArguments(arguments);
        pendingArguments["relative_path"] = target.RelativePath;
        var pending = BuildPending(
            toolName,
            kind,
            pendingArguments,
            target.WorkspaceRoot,
            target.RelativePath,
            target.ExpectedPreImageExisted ? target.ExpectedPreImageSha256 : null,
            target.ExpectedPreImageExisted,
            policy,
            proposedContent);
        return new AgentMutationPreparationResult(pending, string.Empty)
        {
            Content = targetResult.Content,
            Existed = targetResult.Existed
        };
    }

    private static AgentPendingToolAction BuildPending(
        string toolName,
        AgentMutationKind kind,
        Dictionary<string, object?> arguments,
        string workspaceRoot,
        string relativePath,
        string? expectedPreImage,
        bool expectedPreImageExisted,
        WorkspacePolicy? policy,
        string proposedContent = "")
    {
        var pending = new AgentPendingToolAction
        {
            ToolName = toolName,
            Arguments = CloneArguments(arguments),
            ProposalId = Guid.NewGuid().ToString("N"),
            ProposalRevision = 1,
            SchemaVersion = SchemaVersion,
            WorkspaceRoot = workspaceRoot,
            MutationKind = kind,
            RelativePath = relativePath,
            ExpectedPreImageSha256 = expectedPreImage ?? string.Empty,
            ExpectedPreImageExisted = expectedPreImageExisted,
            ProposedContent = proposedContent,
            ProposedContentSha256 = proposedContent.Length == 0 && kind == AgentMutationKind.Command
                ? string.Empty
                : ComputeContentSha256(proposedContent),
            PolicyFingerprint = ComputePolicyFingerprint(policy),
            PreparedAt = DateTime.UtcNow
        };
        pending.Fingerprint = AgentApprovalFingerprint.Compute(pending);
        return pending;
    }

    private static string? ResolveRoot(AgentWorkspaceOptions options, out string? root)
    {
        try
        {
            root = AgentWorkspaceTools.ResolveWorkspaceRoot(options.WorkspaceRoot);
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DirectoryNotFoundException or ArgumentException)
        {
            root = null;
            return $"The workspace root could not be prepared: {ex.Message}";
        }
    }

    private static int EffectiveMaxFileBytes(int configured) => configured > 0 ? configured : 128 * 1024;

    private static string? ValidateKeys(Dictionary<string, object?> arguments, string[] allowed)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in arguments.Keys)
        {
            if (!allowed.Contains(key))
                return $"The mutation contains unknown argument '{key}'.";
            if (!seen.Add(key))
                return $"The mutation contains duplicate argument '{key}' with ambiguous casing.";
        }

        return null;
    }

    private static bool TryGetRequiredString(
        Dictionary<string, object?> arguments,
        string name,
        bool allowEmpty,
        out string value,
        out string error)
    {
        value = string.Empty;
        error = string.Empty;
        if (!arguments.TryGetValue(name, out var raw) || raw is null)
        {
            error = $"The mutation requires string argument '{name}'.";
            return false;
        }

        if (raw is string direct)
            value = direct;
        else if (raw is JsonElement { ValueKind: JsonValueKind.String } element)
            value = element.GetString() ?? string.Empty;
        else
        {
            error = $"Mutation argument '{name}' must be a JSON string, not {DescribeType(raw)}.";
            return false;
        }

        if (!allowEmpty && value.Length == 0)
        {
            error = $"Mutation argument '{name}' cannot be empty.";
            return false;
        }

        return true;
    }

    private static string DescribeType(object value) => value is JsonElement element
        ? element.ValueKind.ToString()
        : value.GetType().Name;

    private static int CountOccurrences(string content, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
