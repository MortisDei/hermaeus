param(
    [string]$TraceFile,
    [string]$SchemaFile
)

$rootDir = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($TraceFile)) {
    $TraceFile = Join-Path $rootDir 'agent.trace.jsonl'
}

if ([string]::IsNullOrWhiteSpace($SchemaFile)) {
    $SchemaFile = Join-Path $rootDir 'docs/schemas/agent_trace.schema.json'
}

dotnet run --project (Join-Path $rootDir 'src/Tools/TraceValidator') -- $TraceFile $SchemaFile
