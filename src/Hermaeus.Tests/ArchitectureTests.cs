using System.Collections.ObjectModel;
using System.Reflection;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// Guards the dependency rules in AGENTS.md. These tests exist so layering
/// violations fail the build instead of surfacing in review.
/// </summary>
public sealed class ArchitectureTests
{
    private static Assembly ViewModels => typeof(Hermaeus.ViewModels.ChatViewModel).Assembly;
    private static Assembly Core => typeof(Hermaeus.Core.Services.ILlmService).Assembly;
    private static Assembly Services => typeof(Hermaeus.Services.RedactionService).Assembly;
    private static Assembly AgentAsm => typeof(Hermaeus.Agent.Services.AgentService).Assembly;
    private static Assembly VoiceAsm => typeof(Hermaeus.Voice.NativeKokoroVoiceProvider).Assembly;

    private static string[] RefNames(Assembly asm) =>
        asm.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

    [Fact]
    public void ViewModels_do_not_reference_Avalonia()
    {
        var offenders = RefNames(ViewModels)
            .Where(n => n.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(offenders.Count == 0,
            $"Hermaeus.ViewModels must stay UI-framework-free; found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void OnnxRuntime_is_confined_to_the_Rag_and_Voice_projects()
    {
        // Services references Hermaeus.Voice for provider wiring (VoiceProviderRegistry),
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
            "Hermaeus.Voice is expected to reference ONNX Runtime directly for native Kokoro inference.");
    }

    [Fact]
    public void Agent_does_not_reference_Rag()
    {
        var offenders = RefNames(AgentAsm)
            .Where(n => n.StartsWith("Hermaeus.Rag", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(offenders.Count == 0,
            $"Hermaeus.Agent must depend on Hermaeus.Core's retrieval interface, not Hermaeus.Rag directly; found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Agent_does_not_reference_Mcp()
    {
        var offenders = RefNames(AgentAsm)
            .Where(n => n.StartsWith("Hermaeus.Mcp", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(offenders.Count == 0,
            $"Hermaeus.Agent must depend on Hermaeus.Core's MCP bridge interface, not Hermaeus.Mcp directly; found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ViewModels_do_not_reference_Mcp()
    {
        var offenders = RefNames(ViewModels)
            .Where(n => n.StartsWith("Hermaeus.Mcp", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(offenders.Count == 0,
            $"Hermaeus.ViewModels must manage MCP servers through settings data only, not Hermaeus.Mcp directly; found: {string.Join(", ", offenders)}");
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
            $"Hermaeus.Core gained unapproved references: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Types provably never bound to an Avalonia ItemsControl. Keep this empty
    /// unless a specific, reviewed exception is needed (r9 03-ui-thread-safety.md 3.3).
    /// </summary>
    private static readonly (Type Type, string Property)[] ObservableCollectionOptOuts = [];

    /// <summary>
    /// r11 1.3: four independent FindOnPath/ResolveExecutable reimplementations
    /// in Hermaeus.Services never tried PATHEXT, so "llama-server" could never
    /// resolve to "llama-server.exe" on Windows. Hermaeus.Services.ProcessManagement.ExecutableResolver
    /// is now the one place PATH/directory probing lives; nothing else in
    /// Hermaeus.Services may declare its own FindOnPath.
    /// </summary>
    [Fact]
    public void Services_do_not_reimplement_FindOnPath()
    {
        var offenders = Services.GetTypes()
            .Where(t => t != typeof(Hermaeus.Services.ProcessManagement.ExecutableResolver))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.Name == "FindOnPath")
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"FindOnPath must live only in ExecutableResolver so PATHEXT resolution can never regress independently; found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ViewModel_collections_are_UI_thread_guarded()
    {
        var offenders = new List<string>();
        foreach (var type in ViewModels.GetTypes().Where(t => t.IsPublic))
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!prop.PropertyType.IsGenericType) continue;
                if (prop.PropertyType.GetGenericTypeDefinition() != typeof(ObservableCollection<>)) continue;
                if (ObservableCollectionOptOuts.Any(o => o.Type == type && o.Property == prop.Name)) continue;
                offenders.Add($"{type.Name}.{prop.Name}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Public ObservableCollection<T> properties on ViewModels must be UiBoundCollection<T> " +
            $"so cross-thread mutation fails loudly instead of corrupting Avalonia's container generator: {string.Join(", ", offenders)}");
    }
}
