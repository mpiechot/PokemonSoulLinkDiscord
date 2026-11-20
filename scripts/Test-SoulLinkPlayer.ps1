#requires -Version 7.0
param([string]$Executable, [switch]$PackageOnly)
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
    try {
        if (-not $process.WaitForExit(35000)) { $process.Kill($true); throw 'Packaged startup timed out.' }
        if ($process.ExitCode -ne 0) {
            $report = if ($Arguments[0] -eq '--smoke-game') { Join-Path $Arguments[1] 'game-error.txt' }
            if ($report -and (Test-Path -LiteralPath $report)) { Get-Content -LiteralPath $report | Write-Host }
            throw "Packaged startup failed: $($process.ExitCode)."
        }
    } finally {
        $process.Dispose()
    }
}

function New-TestRom([string]$Path, [byte]$Revision, [byte]$Species) {
    # A cartridge-sized image keeps mGBA from treating an intentionally small stub as a multiboot ROM.
    $rom = [byte[]]::new(512 * 1024)
    [Text.Encoding]::ASCII.GetBytes('POKEMON RUBYAXVD').CopyTo($rom, 0xA0)
    $rom[0xB2] = 0x96
    $rom[0xBC] = $Revision
    $headerSum = ($rom[0xA0..0xBC] | Measure-Object -Sum).Sum
    $rom[0xBD] = [byte]((-$headerSum - 0x19) -band 0xFF)
    # Original ARM test stub: initialize one live team slot at the German Ruby addresses, then idle.
    $entry = [Convert]::FromHexString('2E0000EA')
    $entry.CopyTo($rom, 0)
    $program = [Convert]::FromHexString('24009FE50110A0E30010C0E51C009FE528108FE21920A0E3043091E4043080E4012052E2FBFFFF1AFEFFFFEA6043000370430003')
    $program.CopyTo($rom, 0xC0)
    $rom[0x108] = 0xBB
    $rom[0x109] = 0xFF
    $rom[0x113] = 2
    $rom[0x11C] = $Species
    $rom[0x120] = $Species
    $rom[0x154] = 5
    $rom[0x156] = 15
    $rom[0x158] = 20
    [IO.File]::WriteAllBytes($Path, $rom)
}
$fresh = Join-Path $testRoot 'fresh'
Run-Player $Executable @('--smoke-test', $fresh)
$result = Get-Content (Join-Path $fresh 'smoke-result.json') -Raw | ConvertFrom-Json
if ($result.client.registered -or $result.client.isDemo -or $result.client.snapshot -or $result.client.run -or -not $result.bridgeIncluded -or -not $result.emulatorIncluded -or $result.browserStarted) { throw 'Player did not start cleanly.' }
$prepared = Join-Path $testRoot 'Prepared.exe'
Run-Player $Executable @('--prepare-player', $prepared, 'https://example.invalid', 'PACKAGE-CHECK', 'Packaging check')
$preparedHome = Join-Path $testRoot 'prepared-startup'
Run-Player $prepared @('--smoke-test', $preparedHome)
$result = Get-Content (Join-Path $preparedHome 'smoke-result.json') -Raw | ConvertFrom-Json
if (-not $result.profileLoaded -or $result.profileName -ne 'Packaging check' -or $result.client.snapshot -or $result.client.isDemo) { throw 'Embedded player profile failed.' }
if ($PackageOnly) {
    Write-Host 'Single-file player verified: clean startup and embedded mGBA, bridge and invitation.'
    return
}
for ($revision = 0; $revision -le 1; $revision++) {
    $romPath = Join-Path $testRoot "Ruby probe $revision.gba"
    $species = 25 + $revision
    New-TestRom $romPath $revision $species
    $player = Join-Path $testRoot "Player $revision.exe"
    Run-Player $Executable @('--prepare-player', $player, 'https://example.invalid', "RUN-$revision", 'Packaging check')
    Run-Player $player @('--smoke-game', $preparedHome, $romPath)
    $game = Get-Content (Join-Path $preparedHome 'game-result.json') -Raw | ConvertFrom-Json
    if ($game.layoutId -ne 'ruby-axvd-reference-v2' -or $game.pokemon.Count -ne 1 -or $game.pokemon[0].species -ne $species -or $game.pokemon[0].hp -ne 15) {
        throw "Live emulator bridge returned incorrect party data for revision $revision."
    }
}
$romSelections = @(Get-ChildItem -LiteralPath $preparedHome -Filter 'rom-*.txt' | Get-Content)
if ($romSelections.Count -ne 2 -or $romSelections[0] -eq $romSelections[1]) { throw 'ROM selection leaked between prepared run profiles.' }
Write-Host 'Single-file player verified: clean startup, prepared registration, isolated ROM choices and real mGBA reads for both German Ruby revisions.'
