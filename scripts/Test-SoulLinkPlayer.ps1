#requires -Version 7.0
param([string]$Executable)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Executable) { $Executable = Join-Path $root 'artifacts\player\SoulLink.exe' }
$testRoot = Join-Path $root ('artifacts\player-check-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
function Run-Player([string]$Path, [string[]]$Arguments) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new($Path)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($startInfo)
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Packaged startup timed out.' }
    if ($process.ExitCode -ne 0) { throw "Packaged startup failed: $($process.ExitCode)." }
}
$fresh = Join-Path $testRoot 'fresh'
Run-Player $Executable @('--smoke-test', $fresh)
$result = Get-Content (Join-Path $fresh 'smoke-result.json') -Raw | ConvertFrom-Json
if ($result.client.registered -or $result.client.isDemo -or $result.client.snapshot -or $result.client.run -or -not $result.bridgeIncluded -or $result.browserStarted) { throw 'Player did not start cleanly.' }
$prepared = Join-Path $testRoot 'Prepared.exe'
Run-Player $Executable @('--prepare-player', $prepared, 'https://example.invalid', 'PACKAGE-CHECK', 'Packaging check')
$preparedHome = Join-Path $testRoot 'prepared-startup'
Run-Player $prepared @('--smoke-test', $preparedHome)
$result = Get-Content (Join-Path $preparedHome 'smoke-result.json') -Raw | ConvertFrom-Json
if (-not $result.profileLoaded -or $result.profileName -ne 'Packaging check' -or $result.client.snapshot -or $result.client.isDemo) { throw 'Embedded player profile failed.' }
Write-Host 'Single-file player verified: clean startup, embedded bridge, embedded join profile, no browser and no demo data.'
