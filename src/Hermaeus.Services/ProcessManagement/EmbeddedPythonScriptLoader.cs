using System.Reflection;

namespace Hermaeus.Services.ProcessManagement;

internal static class EmbeddedPythonScriptLoader
{
    internal static string Load(string resourceName)
    {
        var assembly = typeof(EmbeddedPythonScriptLoader).Assembly;
        var fullName = $"Hermaeus.Services.Resources.Python.{resourceName}";
        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Embedded Python script not found: {fullName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
