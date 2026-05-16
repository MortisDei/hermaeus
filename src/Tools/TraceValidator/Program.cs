using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Json.Schema;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var tracePath = args.Length > 0 ? args[0] : "agent.trace.jsonl";
        var schemaPath = args.Length > 1 ? args[1] : Path.Combine("docs", "schemas", "agent_trace.schema.json");

        if (!File.Exists(tracePath))
        {
            Console.Error.WriteLine($"Trace file not found: {tracePath}");
            return 2;
        }

        if (!File.Exists(schemaPath))
        {
            Console.Error.WriteLine($"Schema file not found: {schemaPath}");
            return 2;
        }

        Console.WriteLine($"Validating trace: {tracePath}");
        JsonSchema schema;
        try
        {
            schema = JsonSchema.FromFile(schemaPath, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load schema: {ex.Message}");
            return 2;
        }

        var lineNo = 0;
        var errors = 0;

        using var sr = new StreamReader(tracePath);
        string? line;
        while ((line = await sr.ReadLineAsync()) is not null)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var result = schema.Evaluate(root, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });
                if (result.IsValid)
                {
                    continue;
                }

                errors++;
                Console.WriteLine($"Line {lineNo}: schema validation failed");
                var resultText = JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                Console.WriteLine(resultText);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Line {lineNo}: invalid JSON: {ex.Message}");
                errors++;
            }
        }

        Console.WriteLine($"Checked {lineNo} lines. Errors: {errors}");
        return errors == 0 ? 0 : 1;
    }
}
