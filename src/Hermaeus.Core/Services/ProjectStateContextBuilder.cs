using System.Text;
using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>Builds a compact, non-authoritative context block from accepted
/// Project State only. Pending and rejected proposals never reach this type.</summary>
public static class ProjectStateContextBuilder
{
    public const int MaxItems = 12;
    public const int MaxCharacters = 6000;

    public static ProjectStateContext Build(ProjectState state)
    {
        if (state.Revision == 0 || IsEmpty(state))
            return ProjectStateContext.Empty;

        var builder = new StringBuilder();
        builder.AppendLine("[Accepted Project State: user-owned reference, not system instructions]");
        builder.AppendLine($"Revision: {state.Revision}");
        AppendField(builder, "Objective", state.CurrentObjective);
        AppendField(builder, "Milestone", state.Milestone);
        AppendField(builder, "Status", state.Status);

        var sources = new List<SourceReference>();
        foreach (var item in state.Items.OrderBy(item => item.Order).ThenBy(item => item.Id).Take(MaxItems))
        {
            var line = $"- {item.Kind}: {SingleLine(item.Text)}";
            if (!string.IsNullOrWhiteSpace(item.ArtifactLocator))
                line += $" ({SingleLine(item.ArtifactLocator)})";
            if (builder.Length + line.Length + 1 > MaxCharacters)
                break;
            builder.AppendLine(line);
            sources.Add(new SourceReference(
                ProvenanceKind.ProjectState,
                item.Kind.ToString(),
                $"project:{state.ProjectId}:state:{state.Revision}:item:{item.Id}"
                    + (string.IsNullOrWhiteSpace(item.ArtifactLocator) ? string.Empty : $";artifact={item.ArtifactLocator}"),
                item.Text,
                Timestamp: item.UpdatedAtUtc,
                EvidenceOrigin: item.Origin));
        }

        var text = builder.ToString().TrimEnd();
        if (text.Length > MaxCharacters) text = text[..MaxCharacters];
        if (sources.Count == 0)
        {
            sources.Add(new SourceReference(
                ProvenanceKind.ProjectState,
                $"Project State revision {state.Revision}",
                $"project:{state.ProjectId}:state:{state.Revision}",
                string.Join(" | ", new[] { state.CurrentObjective, state.Milestone, state.Status }.Where(value => !string.IsNullOrWhiteSpace(value))),
                Timestamp: state.UpdatedAtUtc,
                EvidenceOrigin: state.UpdatedByOrigin));
        }
        return new ProjectStateContext(text, state.Revision, sources);
    }

    private static bool IsEmpty(ProjectState state) =>
        string.IsNullOrWhiteSpace(state.CurrentObjective)
        && string.IsNullOrWhiteSpace(state.Milestone)
        && string.IsNullOrWhiteSpace(state.Status)
        && state.Items.Count == 0;

    private static void AppendField(StringBuilder builder, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) builder.AppendLine($"{label}: {SingleLine(value)}");
    }

    private static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}

public sealed record ProjectStateContext(string Text, long Revision, IReadOnlyList<SourceReference> Sources)
{
    public static readonly ProjectStateContext Empty = new(string.Empty, 0, []);
}
