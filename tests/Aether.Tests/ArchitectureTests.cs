using System.Reflection;
using Xunit;

namespace Aether.Tests;

/// <summary>
/// Guards the dependency rules in AGENTS.md. These tests exist so layering
/// violations fail the build instead of surfacing in review.
/// </summary>
public sealed class ArchitectureTests
{
    private static Assembly ViewModels => typeof(Aether.ViewModels.ChatViewModel).Assembly;
    private static Assembly Core => typeof(Aether.Core.Services.ILlmService).Assembly;
    private static Assembly Services => typeof(Aether.Services.RedactionService).Assembly;
    private static Assembly AgentAsm => typeof(Aether.Agent.Services.AgentService).Assembly;

    private static string[] RefNames(Assembly asm) =>
        asm.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

    [Fact]
    public void ViewModels_do_not_reference_Avalonia()
    {
        var offenders = RefNames(ViewModels)
            .Where(n => n.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(offenders.Count == 0,
            $"Aether.ViewModels must stay UI-framework-free; found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void OnnxRuntime_is_confined_to_the_Rag_project()
    {
        foreach (var asm in new[] { Core, Services, AgentAsm, ViewModels })
        {
            var offenders = RefNames(asm)
                .Where(n => n.StartsWith("Microsoft.ML.OnnxRuntime", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(offenders.Count == 0,
                $"{asm.GetName().Name} must not reference ONNX Runtime; found: {string.Join(", ", offenders)}");
        }
    }

    [Fact]
    public void Core_references_only_approved_assemblies()
    {
        // CommunityToolkit.Mvvm is temporarily tolerated; removing it is a
        // tracked 1.x task. Everything else must be BCL.
        var allowedPrefixes = new[]
        {
            "System", "netstandard", "mscorlib", "Microsoft.CSharp",
            "CommunityToolkit.Mvvm"
        };
        var offenders = RefNames(Core)
            .Where(n => !allowedPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
            .ToList();
        Assert.True(offenders.Count == 0,
            $"Aether.Core gained unapproved references: {string.Join(", ", offenders)}");
    }
}
