param(
    # r29 doc 04 4.6 / 5.2: measured line coverage is 61.6%. The old 47 (and
    # AGENTS.md's 45) could not fail on any regression short of deleting a
    # quarter of the suite. A ratchet that cannot catch a regression is
    # decoration. 60 is just under the real number: a genuine regression trips
    # it, ordinary variance does not.
    [double]$Threshold = 60
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$project = Join-Path $root "src/Hermaeus.Tests/Hermaeus.Tests.csproj"
$resultsDir = Join-Path ([System.IO.Path]::GetTempPath()) "hermaeus-coverage-$([Guid]::NewGuid().ToString('N'))"

try {
    New-Item -ItemType Directory -Force $resultsDir | Out-Null

    dotnet test $project --no-restore --collect:"XPlat Code Coverage" --results-directory $resultsDir `
        "-p:CoverletOutputFormat=cobertura" "-p:Threshold=$Threshold" "-p:ThresholdType=line" "-p:ThresholdStat=total"

    $reportFile = Get-ChildItem -Path $resultsDir -Filter "coverage.cobertura.xml" -Recurse | Select-Object -First 1
    if ($reportFile) {
        Write-Host "Coverage report: $($reportFile.FullName)"
    }
}
finally {
    if (Test-Path -LiteralPath $resultsDir) {
        Remove-Item -LiteralPath $resultsDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
