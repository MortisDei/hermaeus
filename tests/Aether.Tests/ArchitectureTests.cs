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
    private static Assembly VoiceAsm => typeof(Aether.Voice.NativeKokoroVoiceProvider).Assembly;

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
    public void OnnxRuntime_is_confined_to_the_Rag_and_Voice_projects()
    {
        // Services references Aether.Voice for provider wiring (VoiceProviderRegistry),
        // but must not reach past that thin surface to use ONNX Runtime types directly.
        foreach (var asm in new[] { Core, Services, AgentAsm, ViewModels })
        {
            var offenders = RefNames(asm)
                .Where(n => n.StartsWith("Microsoft.ML.OnnxRuntime", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(offenders.Count == 0,
                $"{asm.GetName().Name} must not reference ONNX Runtime; found: {string.Join(", ", offenders)}");
        }

        var voiceOffenders = RefNames(VoiceAsm)
            .Where(n => n.StartsWith("Microsoft.ML.OnnxRuntime", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(voiceOffenders.Count > 0,
            "Aether.Voice is expected to reference ONNX Runtime directly for native Kokoro inference.");
    }

    [Fact]
    public void Agent_does_not_reference_Rag()
    {
        var offenders = RefNames(AgentAsm)
            .Where(n => n.StartsWith("Aether.Rag", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(offenders.Count == 0,
            $"Aether.Agent must depend on Aether.Core's retrieval interface, not Aether.Rag directly; found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Agent_does_not_reference_Mcp()
    {
        var offenders = RefNames(AgentAsm)
            .Where(n => n.StartsWith("Aether.Mcp", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(offenders.Count == 0,
            $"Aether.Agent must depend on Aether.Core's MCP bridge interface, not Aether.Mcp directly; found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ViewModels_do_not_reference_Mcp()
    {
        var offenders = RefNames(ViewModels)
            .Where(n => n.StartsWith("Aether.Mcp", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(offenders.Count == 0,
            $"Aether.ViewModels must manage MCP servers through settings data only, not Aether.Mcp directly; found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Core_references_only_approved_assemblies()
    {
        var allowedPrefixes = new[]
        {
            "System", "netstandard", "mscorlib", "Microsoft.CSharp"
        };
        var offenders = RefNames(Core)
            .Where(n => !allowedPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
            .ToList();
        Assert.True(offenders.Count == 0,
            $"Aether.Core gained unapproved references: {string.Join(", ", offenders)}");
    }
}
