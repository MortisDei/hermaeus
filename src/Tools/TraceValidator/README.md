# TraceValidator

Small CLI tool to validate Aether `agent.trace.jsonl` event lines for basic required fields and timestamp format.

Usage:

```bash
dotnet run --project src/Tools/TraceValidator -- path/to/agent.trace.jsonl [docs/schemas/agent_trace.schema.json]
```

Exit codes:
- 0: success (no validation errors)
- 1: validation errors found
- 2: missing input file

This intentionally performs lightweight validation (required keys and timestamp parsing). For full JSON Schema validation, add a JSON Schema library (e.g., `Json.Schema`) and wire it to the schema file.
