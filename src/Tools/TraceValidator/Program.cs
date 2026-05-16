using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

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
            // Not fatal - continue with minimal validation
        }

        Console.WriteLine($"Validating trace: {tracePath}");
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

                if (!root.TryGetProperty("timestamp", out var ts) || string.IsNullOrWhiteSpace(ts.GetString()))
                {
                    Console.WriteLine($"Line {lineNo}: missing timestamp");
                    errors++;
                    continue;
                }

                if (!DateTime.TryParse(ts.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out _))
                {
                    Console.WriteLine($"Line {lineNo}: invalid timestamp format: {ts.GetString()}");
                    errors++;
                }

                foreach (var required in new[] { "taskId", "event", "status" })
                {
                    if (!root.TryGetProperty(required, out _))
                    {
                        Console.WriteLine($"Line {lineNo}: missing required property '{required}'");
                        errors++;
                    }
                }
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
