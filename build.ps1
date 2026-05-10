param([switch]$SelfContained)

$proj = "src/Aether.Desktop/Aether.Desktop.csproj"
$sc   = if ($SelfContained) { "true" } else { "false" }

Write-Host "Restoring..."
dotnet restore

Write-Host "Building win-x64..."
dotnet publish $proj -c Release -r win-x64 --self-contained $sc -o "dist/win-x64"

Write-Host "Done - binaries in dist/win-x64/"
