# TraceValidator

Small CLI tool to validate Hermaeus `agent.trace.jsonl` event lines against `docs/schemas/agent_trace.schema.json` using `Json.Schema`.

Usage:

```bash
dotnet run --project src/Tools/TraceValidator -- path/to/agent.trace.jsonl [docs/schemas/agent_trace.schema.json]
```

Or use the convenience wrapper:

```bash
bash scripts/validate_trace.sh [path/to/agent.trace.jsonl] [docs/schemas/agent_trace.schema.json]
```

PowerShell users can run:

```powershell
.\scripts\validate_trace.ps1 [path/to/agent.trace.jsonl] [docs/schemas/agent_trace.schema.json]
```

Exit codes:
- 0: success (no validation errors)
- 1: validation errors found
- 2: missing input file or schema load failure

Validation errors are printed using the schema evaluation output so you can see the failing line and the validation details.
