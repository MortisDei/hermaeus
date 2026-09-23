param(
    # r29 doc 04 4.6 / 5.2: measured line coverage is 61.6%. The prior lower
    # ratchets could not fail on any regression short of deleting a quarter of
    # the suite. A ratchet that cannot catch a regression is decoration. 60 is
    # just under the real number: a genuine regression trips it, ordinary
    # variance does not.
    [ValidateRange(0, 100)][double]$Threshold = 60
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$project = Join-Path $root "src/Hermaeus.Tests/Hermaeus.Tests.csproj"
$resultsDir = Join-Path ([System.IO.Path]::GetTempPath()) "hermaeus-coverage-$([Guid]::NewGuid().ToString('N'))"

try {
    New-Item -ItemType Directory -Force $resultsDir | Out-Null

    dotnet test $project --no-restore --collect:"XPlat Code Coverage" --results-directory $resultsDir
    if ($LASTEXITCODE -ne 0) {
        throw "Coverage tests failed with exit code $LASTEXITCODE."
    }

    $reportFile = Get-ChildItem -Path $resultsDir -Filter "coverage.cobertura.xml" -Recurse | Select-Object -First 1
    if (-not $reportFile) {
        throw "Coverage test run produced no Cobertura report."
    }

    [xml]$report = Get-Content -LiteralPath $reportFile.FullName -Raw
    $covered = 0L
    $valid = 0L
    if (-not [long]::TryParse($report.DocumentElement.GetAttribute("lines-covered"), [ref]$covered) -or
        -not [long]::TryParse($report.DocumentElement.GetAttribute("lines-valid"), [ref]$valid) -or
        $valid -le 0 -or $covered -lt 0 -or $covered -gt $valid) {
        throw "Coverage report has invalid line counts."
    }

    $percent = 100.0 * $covered / $valid
    Write-Host ("Line coverage: {0:F2}% ({1}/{2}); floor {3}%." -f $percent, $covered, $valid, $Threshold)
    if ($percent -lt $Threshold) {
        throw "Line coverage is below the $Threshold% floor."
    }
    Write-Host "Coverage report: $($reportFile.FullName)"
}
finally {
    if (Test-Path -LiteralPath $resultsDir) {
        Remove-Item -LiteralPath $resultsDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
