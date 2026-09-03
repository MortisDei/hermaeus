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
$appDir = Join-Path $packageDir "app"
$iconDir = Join-Path $packageDir "icons"
$localApiDir = Join-Path $appDir "LocalApi"
$launcherSourceDir = Join-Path $root "src/Hermaeus.Launcher"
$launcherBuildDir = Join-Path $dist ".launcher-$Runtime"
$launcherPath = Join-Path $packageDir "Hermaeus.exe"

function Remove-BuildTemporaryOutput {
    foreach ($path in @($publishDir, $localApiPublishDir, $launcherBuildDir)) {
        if (Test-Path $path) {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Assert-RuntimeRestoreTarget([string]$ProjectPath, [string]$TargetRuntime) {
    $assetsPath = Join-Path (Split-Path -Parent $ProjectPath) "obj/project.assets.json"
    $targetName = "net10.0/$TargetRuntime"
    if (-not (Test-Path $assetsPath -PathType Leaf)) {
        throw "-SkipRestore was requested, but restore assets are missing for '$ProjectPath'. Run this script without -SkipRestore first."
    }

    try {
        $assets = Get-Content -Raw $assetsPath | ConvertFrom-Json
    } catch {
        throw "-SkipRestore was requested, but restore assets could not be read for '$ProjectPath'. Run this script without -SkipRestore first."
    }

    $hasTarget = $assets.targets.PSObject.Properties.Name -contains $targetName
    if (-not $hasTarget) {
        throw "-SkipRestore was requested, but '$ProjectPath' is not restored for '$TargetRuntime'. Run 'pwsh ./build.ps1 -Runtime $TargetRuntime' first."
    }
}

function Import-MsvcEnvironment([string]$TargetRuntime) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio/Installer/vswhere.exe"
    if (-not (Test-Path $vswhere -PathType Leaf)) {
        throw "Visual Studio Build Tools are required to build the native Windows launcher."
    }

    $requiredComponent = if ($TargetRuntime -eq "win-arm64") {
        "Microsoft.VisualStudio.Component.VC.Tools.ARM64"
    } else {
        "Microsoft.VisualStudio.Component.VC.Tools.x86.x64"
    }
    $installationPath = & $vswhere -latest -products * -requires $requiredComponent -property installationPath
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($installationPath)) {
        throw "Visual Studio C++ tools for $TargetRuntime are required to build the native Windows launcher."
    }

    $devShellModule = Join-Path $installationPath "Common7/Tools/Microsoft.VisualStudio.DevShell.dll"
    if (-not (Test-Path $devShellModule -PathType Leaf)) {
        throw "Could not locate the Visual Studio Developer PowerShell module."
    }

    $targetArchitecture = if ($TargetRuntime -eq "win-arm64") { "arm64" } else { "amd64" }
    try {
        Import-Module $devShellModule -Force -ErrorAction Stop
        $null = Enter-VsDevShell `
            -VsInstallPath $installationPath `
            -SkipAutomaticLocation `
            -Arch $targetArchitecture `
            -HostArch "amd64" `
            -ErrorAction Stop
    } catch {
        throw "Could not initialize the Visual Studio C++ build environment: $($_.Exception.Message)"
    }
}

function Build-NativeLauncher([string]$TargetRuntime, [string]$OutputPath) {
    if ($TargetRuntime -notin @("win-x64", "win-arm64")) {
        throw "The native Windows launcher supports win-x64 and win-arm64, not '$TargetRuntime'."
    }

    New-Item -ItemType Directory -Force $launcherBuildDir | Out-Null
    $objectPath = Join-Path $launcherBuildDir "launcher.obj"
    $resourcePath = Join-Path $launcherBuildDir "launcher.res"

    Push-Location $launcherSourceDir
    try {
        if ($IsWindows) {
            Import-MsvcEnvironment $TargetRuntime
            $resourceCompiler = (Get-Command "rc.exe" -ErrorAction Stop).Source
            $compiler = (Get-Command "cl.exe" -ErrorAction Stop).Source
            $linker = (Get-Command "link.exe" -ErrorAction Stop).Source

            & $resourceCompiler /nologo "/fo$resourcePath" "launcher.rc"
            & $compiler /nologo /c /TC /O1 /Os /GS- /W4 /WX /DUNICODE /D_UNICODE "/Fo$objectPath" "launcher.c"
            & $linker /nologo /SUBSYSTEM:WINDOWS /ENTRY:wWinMainCRTStartup /NODEFAULTLIB "/OUT:$OutputPath" $objectPath $resourcePath kernel32.lib user32.lib
        } else {
            if ($TargetRuntime -ne "win-x64") {
                throw "Cross-building the $TargetRuntime launcher requires a Windows host with Visual Studio C++ tools."
            }

            $resourceCompiler = (Get-Command "x86_64-w64-mingw32-windres" -ErrorAction Stop).Source
            $compiler = (Get-Command "x86_64-w64-mingw32-gcc" -ErrorAction Stop).Source
            & $resourceCompiler "launcher.rc" -O coff -o $resourcePath
            & $compiler -mwindows -Os -s -fno-stack-protector -nostdlib `
                "-Wl,--subsystem,windows,--entry,wWinMainCRTStartup" `
                -o $OutputPath "launcher.c" $resourcePath -lkernel32 -luser32
        }
    } finally {
        Pop-Location
    }
}

function Assert-WindowsPackageLayout([string]$PackagePath) {
    $requiredFiles = @(
        "Hermaeus.exe",
        "app/Hermaeus.Desktop.exe",
        "app/Hermaeus.Desktop.dll",
        "app/Hermaeus.Desktop.runtimeconfig.json",
        "app/LocalApi/Hermaeus.LocalApi.exe",
        "app/LocalApi/Hermaeus.LocalApi.dll",
        "docs/README.md",
        "docs/user-guide.md",
        "docs/LICENSE.md",
        "docs/NOTICE.md",
        "docs/COMMERCIAL.md",
        "icons/hermaeus.ico",
        "icons/hermaeus-app.png",
        "icons/hermaeus-tray.png",
        "icons/hermaeus-tray-dark.png",
        "icons/hermaeus-tray-light.png"
    )
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path (Join-Path $PackagePath $relativePath) -PathType Leaf)) {
            throw "Windows package is missing required file '$relativePath'."
        }
    }

    $unexpectedRootFiles = @(Get-ChildItem $PackagePath -File | Where-Object Name -ne "Hermaeus.exe")
    if ($unexpectedRootFiles.Count -ne 0) {
        throw "Windows package root contains unexpected files: $($unexpectedRootFiles.Name -join ', ')."
    }
    if (Test-Path (Join-Path $PackagePath "Launch-Hermaeus.cmd")) {
        throw "Windows package still contains the retired command launcher."
    }
    if (@(Get-ChildItem $PackagePath -Filter "*.pdb" -File -Recurse).Count -ne 0) {
        throw "Windows package contains PDB files."
    }
}

Remove-BuildTemporaryOutput

if (-not $SkipRestore) {
    Write-Host "Restoring..."
    dotnet restore $project -r $Runtime
    dotnet restore $localApiProject -r $Runtime
} else {
    Assert-RuntimeRestoreTarget $project $Runtime
    Assert-RuntimeRestoreTarget $localApiProject $Runtime
}

$buildSucceeded = $false
try {
    Write-Host "Publishing $Runtime ($Configuration, self-contained=$selfContainedValue)..."
    Remove-Item -LiteralPath $packageDir, $archive, $checksum -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $packageDir, $publishDir, $localApiPublishDir, $docDir, $appDir, $iconDir, $localApiDir | Out-Null

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

Copy-Item -Path (Join-Path $publishDir "*") -Destination $appDir -Recurse -Force
Copy-Item -Path (Join-Path $localApiPublishDir "*") -Destination $localApiDir -Recurse -Force
Copy-Item (Join-Path $root "README.md") (Join-Path $docDir "README.md") -Force
Copy-Item (Join-Path $root "docs/user-guide.md") (Join-Path $docDir "user-guide.md") -Force
Copy-Item (Join-Path $root "LICENSE.md") (Join-Path $docDir "LICENSE.md") -Force
Copy-Item (Join-Path $root "NOTICE.md") (Join-Path $docDir "NOTICE.md") -Force
Copy-Item (Join-Path $root "COMMERCIAL.md") (Join-Path $docDir "COMMERCIAL.md") -Force
Get-ChildItem (Join-Path $root "docs") -Filter "hermaeus-branding.*" -File -ErrorAction SilentlyContinue |
    Copy-Item -Destination $docDir -Force
Copy-Item (Join-Path $root "src/Hermaeus.Desktop/Assets/hermaeus.ico") (Join-Path $iconDir "hermaeus.ico") -Force
Copy-Item (Join-Path $root "src/Hermaeus.Desktop/Assets/hermaeus-app.png") (Join-Path $iconDir "hermaeus-app.png") -Force
Copy-Item (Join-Path $root "src/Hermaeus.Desktop/Assets/hermaeus-tray.png") (Join-Path $iconDir "hermaeus-tray.png") -Force
Copy-Item (Join-Path $root "src/Hermaeus.Desktop/Assets/hermaeus-tray-dark.png") (Join-Path $iconDir "hermaeus-tray-dark.png") -Force
Copy-Item (Join-Path $root "src/Hermaeus.Desktop/Assets/hermaeus-tray-light.png") (Join-Path $iconDir "hermaeus-tray-light.png") -Force

Write-Host "Building native Windows launcher..."
Build-NativeLauncher $Runtime $launcherPath

Get-ChildItem $packageDir -Filter "*.pdb" -File -Recurse -ErrorAction SilentlyContinue |
    Remove-Item -Force

Assert-WindowsPackageLayout $packageDir
Remove-Item -Recurse -Force $publishDir, $localApiPublishDir, $launcherBuildDir

    Write-Host "Creating $archive..."
    Compress-Archive -Path $packageDir -DestinationPath $archive -Force

    $hash = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
    $archiveName = Split-Path -Leaf $archive
    "$hash  $archiveName" | Set-Content -NoNewline -Encoding ASCII $checksum

    $buildSucceeded = $true
    Write-Host "Package ready:"
    Write-Host "  $packageDir"
    Write-Host "  $archive"
    Write-Host "  $checksum"
} finally {
    Remove-BuildTemporaryOutput
    if (-not $buildSucceeded) {
        Remove-Item -LiteralPath $packageDir, $archive, $checksum -Recurse -Force -ErrorAction SilentlyContinue
        Write-Warning "Packaging failed; incomplete Windows package output was removed."
    }
}
