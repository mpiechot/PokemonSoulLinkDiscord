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

function New-CompressedFill([int]$Length, [byte]$Value) {
    $bytes = [Collections.Generic.List[byte]]::new()
    $bytes.AddRange([byte[]]@(0x10, ($Length -band 255), ($Length -shr 8), 0))
    $written = 0
    while ($written -lt $Length) {
        $flagPosition = $bytes.Count
        $bytes.Add(0)
        $flags = 0
        for ($bit = 7; $bit -ge 0 -and $written -lt $Length; $bit--) {
            $remaining = $Length - $written
            if ($written -eq 0 -or $remaining -lt 3) {
                $bytes.Add($Value)
                $written++
            } else {
                $match = [Math]::Min($remaining, 18)
                $flags = $flags -bor (1 -shl $bit)
                $bytes.Add([byte](($match - 3) -shl 4))
                $bytes.Add(0)
                $written += $match
            }
        }
        $bytes[$flagPosition] = [byte]$flags
    }
    return ,$bytes.ToArray()
}

function New-TestRom([string]$Path, [byte]$Revision, [byte]$Species) {
    # A cartridge-sized image keeps mGBA from treating an intentionally small stub as a multiboot ROM.
    $rom = [byte[]]::new(4 * 1024 * 1024)
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
    # An original single-color tile and a map of that tile test the cartridge decoder without game artwork.
    $rom[0x3F213A] = 0x1F
    (New-CompressedFill 64 0x71).CopyTo($rom, 0x3F2178)
    (New-CompressedFill 4096 0).CopyTo($rom, 0x3F2EDC)
    [IO.File]::WriteAllBytes($Path, $rom)
}
$fresh = Join-Path $testRoot 'fresh'
Run-Player $Executable @('--smoke-test', $fresh)
$result = Get-Content (Join-Path $fresh 'smoke-result.json') -Raw | ConvertFrom-Json
if ($result.client.registered -or $result.client.isDemo -or $result.client.snapshot -or $result.client.run -or -not $result.bridgeIncluded -or -not $result.emulatorIncluded -or $result.browserStarted) { throw 'Player did not start cleanly.' }
$prepared = Join-Path $testRoot 'Prepared.exe'
Run-Player $Executable @('--prepare-player', $prepared, 'https://example.invalid', 'PACKAGE-CHECK')
$preparedHome = Join-Path $testRoot 'prepared-startup'
Run-Player $prepared @('--smoke-test', $preparedHome)
$result = Get-Content (Join-Path $preparedHome 'smoke-result.json') -Raw | ConvertFrom-Json
if (-not $result.profileLoaded -or $result.profileName -or $result.client.player -or $result.client.snapshot -or $result.client.isDemo) { throw 'Embedded run invitation should leave the player identity unset.' }
if ($PackageOnly) {
    Write-Host 'Single-file player verified: clean startup and embedded mGBA, bridge and invitation.'
    return
}
for ($revision = 0; $revision -le 1; $revision++) {
    $romPath = Join-Path $testRoot "Ruby probe $revision.gba"
    $species = 25 + $revision
    New-TestRom $romPath $revision $species
    $player = Join-Path $testRoot "Player $revision.exe"
    Run-Player $Executable @('--prepare-player', $player, 'https://example.invalid', "RUN-$revision")
    $mapResult = Join-Path $preparedHome 'rubymap-result.txt'
    if (Test-Path -LiteralPath $mapResult) { Remove-Item -LiteralPath $mapResult }
    Run-Player $player @('--smoke-game', $preparedHome, $romPath)
    $game = Get-Content (Join-Path $preparedHome 'game-result.json') -Raw | ConvertFrom-Json
    if ($game.layoutId -ne 'ruby-axvd-reference-v2' -or $game.pokemon.Count -ne 1 -or $game.pokemon[0].species -ne $species -or $game.pokemon[0].hp -ne 15) {
        throw "Live emulator bridge returned incorrect party data for revision $revision."
    }
    if ((Get-Content -LiteralPath $mapResult -Raw) -ne '224×120') {
        throw "The original-map decoder failed for revision $revision."
    }
}
$romSelections = @(Get-ChildItem -LiteralPath $preparedHome -Filter 'rom-*.txt' | Get-Content)
if ($romSelections.Count -ne 2 -or $romSelections[0] -eq $romSelections[1]) { throw 'ROM selection leaked between prepared run profiles.' }
Write-Host 'Single-file player verified: clean startup, prepared invitation, isolated ROM choices, and real mGBA read, dock and detach for both German Ruby revisions.'
