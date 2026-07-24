<#
.SYNOPSIS
Prints the body of the "## [<version>] - <date>" section from the changelog.

.DESCRIPTION
Everything after the heading line, up to but not including the next "## ["
heading, trimmed of leading/trailing blank lines.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$ChangelogPath
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $ChangelogPath) {
    $ChangelogPath = Join-Path $root "CHANGELOG.md"
}

if (-not (Test-Path $ChangelogPath)) {
    [Console]::Error.WriteLine("release-notes: changelog not found: $ChangelogPath")
    exit 1
}

$lines = Get-Content -Path $ChangelogPath
$heading = "## [$Version]"
$found = $false
$section = New-Object System.Collections.Generic.List[string]
$printing = $false

foreach ($line in $lines) {
    if ($line.StartsWith($heading)) {
        $found = $true
        $printing = $true
        continue
    }
    if ($printing -and $line.StartsWith("## [")) {
        $printing = $false
    }
    if ($printing) {
        $section.Add($line)
    }
}

if (-not $found) {
    [Console]::Error.WriteLine("release-notes: no changelog section found for version $Version in $ChangelogPath")
    exit 1
}

$start = 0
while ($start -lt $section.Count -and $section[$start].Trim() -eq "") { $start++ }
$last = $section.Count - 1
while ($last -ge $start -and $section[$last].Trim() -eq "") { $last-- }

for ($i = $start; $i -le $last; $i++) {
    Write-Output $section[$i]
}
exit 0
