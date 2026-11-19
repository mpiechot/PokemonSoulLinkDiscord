param([switch]$Verify, [switch]$Headless)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build SoulLink.Demo/SoulLink.Demo.csproj --verbosity minimal -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Demo build failed.' }
    $demoArguments = @()
    if ($Verify) { $demoArguments += '--verify' }
    if ($Headless) { $demoArguments += '--headless' }
    dotnet SoulLink.Demo/bin/Debug/net9.0/SoulLink.Demo.dll @demoArguments
    if ($LASTEXITCODE -ne 0) { throw 'Demo failed.' }
} finally {
    Pop-Location
}
