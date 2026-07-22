param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SelfContained,
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $true
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src/Hermaeus.Desktop/Hermaeus.Desktop.csproj"
$localApiProject = Join-Path $root "src/Hermaeus.LocalApi/Hermaeus.LocalApi.csproj"
$dist = Join-Path $root "dist"
$propsPath = Join-Path $root "Directory.Build.props"
$props = [xml](Get-Content -Raw $propsPath)
$versionPrefix = $props.Project.PropertyGroup.VersionPrefix
$versionSuffix = $props.Project.PropertyGroup.VersionSuffix

if ([string]::IsNullOrWhiteSpace($versionPrefix)) {
    throw "Could not read VersionPrefix from Directory.Build.props."
}

$version = $versionPrefix
if (-not [string]::IsNullOrWhiteSpace($versionSuffix)) {
    $version = "$version-$versionSuffix"
}

$selfContainedValue = if ($SelfContained) { "true" } else { "false" }
$packageName = "hermaeus-$version-$Runtime"
$packageDir = Join-Path $dist $packageName
$publishDir = Join-Path $dist ".publish-$Runtime"
$localApiPublishDir = Join-Path $dist ".publish-localapi-$Runtime"
$archive = Join-Path $dist "$packageName.zip"
$checksum = "$archive.sha256"
$docDir = Join-Path $packageDir "docs"
$localApiDir = Join-Path $packageDir "LocalApi"

if (-not $SkipRestore) {
    Write-Host "Restoring..."
    dotnet restore $project -r $Runtime
    dotnet restore $localApiProject -r $Runtime
}

Write-Host "Publishing $Runtime ($Configuration, self-contained=$selfContainedValue)..."
Remove-Item -Recurse -Force $packageDir, $publishDir, $localApiPublishDir, $archive, $checksum -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $packageDir, $publishDir, $localApiPublishDir, $docDir, $localApiDir | Out-Null

$publishArgs = @(
    $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-o", $publishDir,
    "--no-restore",
    "-p:UseSharedCompilation=false",
    "-m:1"
)

dotnet publish @publishArgs

$localApiPublishArgs = @(
    $localApiProject,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-o", $localApiPublishDir,
    "--no-restore",
    "-p:UseSharedCompilation=false",
    "-m:1"
)

dotnet publish @localApiPublishArgs

Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageDir -Recurse -Force
Copy-Item -Path (Join-Path $localApiPublishDir "*") -Destination $localApiDir -Recurse -Force
Copy-Item (Join-Path $root "README.md") (Join-Path $docDir "README.md") -Force
Copy-Item (Join-Path $root "LICENSE.md") (Join-Path $docDir "LICENSE.md") -Force
Copy-Item (Join-Path $root "NOTICE.md") (Join-Path $docDir "NOTICE.md") -Force
Copy-Item (Join-Path $root "COMMERCIAL.md") (Join-Path $docDir "COMMERCIAL.md") -Force
Get-ChildItem (Join-Path $root "docs") -Filter "hermaeus-branding.*" -File -ErrorAction SilentlyContinue |
    Copy-Item -Destination $docDir -Force
Copy-Item (Join-Path $root "src/Hermaeus.Desktop/Assets/hermaeus.ico") (Join-Path $packageDir "hermaeus.ico") -Force
Copy-Item (Join-Path $root "src/Hermaeus.Desktop/Assets/hermaeus-app.png") (Join-Path $packageDir "hermaeus-app.png") -Force
Copy-Item (Join-Path $root "src/Hermaeus.Desktop/Assets/hermaeus-tray.png") (Join-Path $packageDir "hermaeus-tray.png") -Force
Copy-Item (Join-Path $root "src/Hermaeus.Desktop/Assets/hermaeus-tray-dark.png") (Join-Path $packageDir "hermaeus-tray-dark.png") -Force
Copy-Item (Join-Path $root "src/Hermaeus.Desktop/Assets/hermaeus-tray-light.png") (Join-Path $packageDir "hermaeus-tray-light.png") -Force

@'
@echo off
setlocal
cd /d "%~dp0"
start "" "%~dp0Hermaeus.Desktop.exe"
'@ | Set-Content -NoNewline -Encoding ASCII (Join-Path $packageDir "Launch-Hermaeus.cmd")

Remove-Item -Recurse -Force $publishDir, $localApiPublishDir

Write-Host "Creating $archive..."
Compress-Archive -Path $packageDir -DestinationPath $archive -Force

$hash = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
$archiveName = Split-Path -Leaf $archive
"$hash  $archiveName" | Set-Content -NoNewline -Encoding ASCII $checksum

Write-Host "Package ready:"
Write-Host "  $packageDir"
Write-Host "  $archive"
Write-Host "  $checksum"
