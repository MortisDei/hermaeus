param(
    [double]$Threshold = 43
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$project = Join-Path $root "src/Aether.Tests/Aether.Tests.csproj"
$resultsDir = Join-Path $root "dist/.coverage"

Remove-Item -Recurse -Force $resultsDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $resultsDir | Out-Null

dotnet test $project --collect:"XPlat Code Coverage" --results-directory $resultsDir `
    "-p:CoverletOutputFormat=cobertura" "-p:Threshold=$Threshold" "-p:ThresholdType=line" "-p:ThresholdStat=total"

$reportFile = Get-ChildItem -Path $resultsDir -Filter "coverage.cobertura.xml" -Recurse | Select-Object -First 1
if ($reportFile) {
    Write-Host "Coverage report: $($reportFile.FullName)"
}
