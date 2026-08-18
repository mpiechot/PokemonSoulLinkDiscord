[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repositoryRoot "PokeSoulLinkBot\PokeSoulLinkBot.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\publish\PokeSoulLinkBot"
}

$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$projectDirectory = [System.IO.Path]::GetDirectoryName($projectPath)
$binDirectory = [System.IO.Path]::GetFullPath((Join-Path $projectDirectory "bin"))
$objDirectory = [System.IO.Path]::GetFullPath((Join-Path $projectDirectory "obj"))

if ($resolvedOutputDirectory.StartsWith($binDirectory, [System.StringComparison]::OrdinalIgnoreCase) -or
    $resolvedOutputDirectory.StartsWith($objDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The publish output must be outside the project's bin and obj directories."
}

$dotnetVersion = & dotnet --version
if ($LASTEXITCODE -ne 0) {
    throw "The .NET SDK could not be executed."
}

$dotnetMajorVersion = [int]($dotnetVersion.Split('.')[0])
if ($dotnetMajorVersion -lt 9) {
    throw ".NET SDK 9 or newer is required. Installed version: $dotnetVersion"
}

$publishArguments = @(
    "publish",
    $projectPath,
    "--configuration",
    "Release",
    "--output",
    $resolvedOutputDirectory,
    "--no-self-contained",
    "-p:UseAppHost=false"
)

if ($NoRestore) {
    $publishArguments += "--no-restore"
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$requiredFiles = [System.Collections.Generic.List[string]]@(
    "PokeSoulLinkBot.dll",
    "PokeSoulLinkBot.deps.json",
    "PokeSoulLinkBot.runtimeconfig.json"
)

$resourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectDirectory "Resources") -File
foreach ($resourceFile in $resourceFiles) {
    $requiredFiles.Add("Resources\$($resourceFile.Name)")
}

$dataFiles = Get-ChildItem -LiteralPath (Join-Path $projectDirectory "Data") -File
foreach ($dataFile in $dataFiles) {
    $requiredFiles.Add("Data\$($dataFile.Name)")
}

$missingFiles = @(
    $requiredFiles |
        Where-Object { -not (Test-Path -LiteralPath (Join-Path $resolvedOutputDirectory $_) -PathType Leaf) }
)

if ($missingFiles.Count -gt 0) {
    throw "The publish output is incomplete. Missing: $($missingFiles -join ', ')"
}

Write-Host "Published PokeSoulLinkBot to $resolvedOutputDirectory"
Write-Host "Start with: dotnet `"$resolvedOutputDirectory\PokeSoulLinkBot.dll`""
