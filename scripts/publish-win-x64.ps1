param(
    [string]$Version
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\GuitarPractice.App\GuitarPractice.App.csproj"
$output = Join-Path $root "artifacts\win-x64"
$licenses = Join-Path $root "licenses"
$dotnetRoot = Split-Path (Get-Command dotnet).Source

$publishArguments = @(
    "publish", $project,
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--output", $output,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
)
if ($Version) {
    $publishArguments += "-p:Version=$Version"
}

dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $dotnetRoot "LICENSE.txt") `
    -Destination (Join-Path $output "DOTNET-LICENSE.txt") `
    -Force
Copy-Item -LiteralPath (Join-Path $dotnetRoot "ThirdPartyNotices.txt") `
    -Destination (Join-Path $output "DOTNET-THIRD-PARTY-NOTICES.txt") `
    -Force
Copy-Item -LiteralPath (Join-Path $licenses "NAudio-LICENSE.txt") `
    -Destination (Join-Path $output "NAudio-LICENSE.txt") `
    -Force
Copy-Item -LiteralPath (Join-Path $licenses "WPF-LICENSE.txt") `
    -Destination (Join-Path $output "WPF-LICENSE.txt") `
    -Force

Write-Host "Windows build created at: $output"
