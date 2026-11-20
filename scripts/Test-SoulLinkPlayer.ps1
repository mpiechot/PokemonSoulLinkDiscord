#requires -Version 7.0
param([string]$Executable, [switch]$PackageOnly, [switch]$ShutdownOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Executable) { $Executable = Join-Path $root 'artifacts\player\SoulLink.exe' }
$tempRoot = if ($PackageOnly) { [IO.Path]::GetFullPath([IO.Path]::GetTempPath()) } else { Join-Path $root 'artifacts' }
$testRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ('soullink-player-check-' + [Guid]::NewGuid().ToString('N'))))
function Run-Player([string]$Path, [string[]]$Arguments) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new($Path)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        if (-not $process.WaitForExit(35000)) {
            $progress = if ($Arguments[0] -eq '--smoke-close') { Join-Path $Arguments[1] 'shutdown-progress.txt' }
            if ($progress -and (Test-Path -LiteralPath $progress)) { Get-Content -LiteralPath $progress | Write-Host }
            $process.Kill($true)
            $process.WaitForExit()
            throw "Packaged check timed out ($($Arguments[0]))."
        }
        if ($process.ExitCode -ne 0) {
            $report = if ($Arguments[0] -eq '--smoke-test') { Join-Path $Arguments[1] 'package-error.txt' }
                elseif ($Arguments[0] -in '--smoke-game','--smoke-pc-items','--smoke-close') { Join-Path $Arguments[1] 'game-error.txt' }
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

function New-TestRom([string]$Path, [byte]$Revision, [byte]$Species, [switch]$EmptyParty) {
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
    if ($EmptyParty) { $rom[0xC4] = 0 }
    $rom[0x108] = 0xBB
    $rom[0x109] = 0xFF
    $rom[0x113] = 2
    $rom[0x11C] = $Species
    $rom[0x120] = $Species
    $rom[0x154] = 5
    $rom[0x156] = 15
    $rom[0x158] = 20
    # Branch from the party initializer to a short series of 32-bit writes.
    [BitConverter]::GetBytes(0xEA000054u).CopyTo($rom, 0xE8)
    $writes = [ordered]@{
        '02025BCC' = 0x03B60044u # First PC item slot: 950 Rare Candies.
        '0202E828' = 0x08000600u # Map header: pointer to a 28 x 24 layout.
        '030048B0' = 0x00010001u # Active player object.
        '030048C0' = 0x000E000Cu # Live (5, 7), padding of seven metatiles.
    }
    [BitConverter]::GetBytes(28).CopyTo($rom, 0x600)
    [BitConverter]::GetBytes(24).CopyTo($rom, 0x604)
    [Text.Encoding]::ASCII.GetBytes('SoulLinkPCFixture').CopyTo($rom, 0x700)
    if ($Revision -eq 1) {
        $rom[0x156] = 0
        $writes['03001BAC'] = 0x200u # Main.inBattle.
        $writes['020239F8'] = 8u # Trainer battle.
        $writes['02024A68'] = 2u # Battler count; first battler maps to party slot zero.
        $writes['02024A70'] = 0x01000000u # Opposing side for battler one.
        $writes['02024AAC'] = 20u # Downed battler's max HP.
        $writes['02024AD8'] = 25u # Opposing Pikachu.
        $writes['02024C3C'] = 33u # Last landed move: Tackle.
        $writes['02024C5C'] = 1u # Target was hit by battler one.
        $writes['0202FF5C'] = 0x10000u # Trainer ID one.
        [byte[]]$trainerName = 0xC7,0xBB,0xD2,0xFF
        $trainerName.CopyTo($rom, 0x1FD478 + 40 + 4)
    }
    $cursor = 0x240
    foreach ($address in $writes.Keys) {
        # ARM instructions: load address and value, store, jump past the literals.
        [uint[]]$setter = 0xE59F0008u,0xE59F1008u,0xE5801000u,0xEA000001u,([Convert]::ToUInt32($address, 16)),$writes[$address]
        [Buffer]::BlockCopy($setter, 0, $rom, $cursor, 24)
        $cursor += 24
    }
    [BitConverter]::GetBytes(0xEAFFFFFEu).CopyTo($rom, $cursor)
    # An original single-color tile and a map of that tile test the cartridge decoder without game artwork.
    $rom[0x3F213A] = 0x1F
    (New-CompressedFill 64 0x71).CopyTo($rom, 0x3F2178)
    (New-CompressedFill 4096 0).CopyTo($rom, 0x3F2EDC)
    [IO.File]::WriteAllBytes($Path, $rom)
}
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
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
        $shutdownHome = Join-Path $testRoot "shutdown-$revision"
        $shutdownArguments = @('--smoke-close', $shutdownHome, $romPath)
        if ($revision -eq 1) { $shutdownArguments += 'detached' }
        Run-Player $player $shutdownArguments
        if (-not (Test-Path -LiteralPath (Join-Path $shutdownHome 'shutdown-result.txt'))) {
            throw "The player shutdown was not verified for revision $revision."
        }
        if ($ShutdownOnly) { continue }
        $mapResult = Join-Path $preparedHome 'rubymap-result.txt'
        if (Test-Path -LiteralPath $mapResult) { Remove-Item -LiteralPath $mapResult }
        Run-Player $player @('--smoke-pc-items', $preparedHome, $romPath)
        $game = Get-Content (Join-Path $preparedHome 'game-result.json') -Raw | ConvertFrom-Json
        if ($game.layoutId -ne 'ruby-axvd-reference-v2' -or $game.pokemon.Count -ne 1 -or $game.pokemon[0].species -ne $species) {
            throw "Live emulator bridge returned incorrect party data for revision $revision."
        }
        if ($revision -eq 0 -and ($game.pokemon[0].hp -ne 15 -or $game.position.x -ne 5 -or $game.position.y -ne 7)) {
            throw 'Live emulator bridge did not read the expected coordinates.'
        }
        if ($revision -eq 1 -and ($game.pokemon[0].hp -ne 0 -or -not $game.inBattle -or $game.faints.Count -ne 1 -or
            $game.faints[0].opponentSpecies -ne 25 -or $game.faints[0].move -ne 33 -or $game.faints[0].trainer -ne 'MAX')) {
            throw 'Live emulator bridge did not read the expected battle context.'
        }
        if ((Get-Content -LiteralPath $mapResult -Raw) -ne '224×120') {
            throw "The original-map decoder failed for revision $revision."
        }
    }
    if ($ShutdownOnly) {
        Write-Host 'One close exited the owned emulator and released the bridge port in both docked and detached states.'
        return
    }
    $emptyRom = Join-Path $testRoot 'Ruby empty party.gba'
    New-TestRom $emptyRom 0 25 -EmptyParty
    $emptyHome = Join-Path $testRoot 'empty-party'
    Run-Player $Executable @('--smoke-close', $emptyHome, $emptyRom, 'empty')
    if (-not (Test-Path -LiteralPath (Join-Path $emptyHome 'shutdown-result.txt'))) { throw 'Empty-party startup was not verified.' }
    $romSelections = @(Get-ChildItem -LiteralPath $preparedHome -Filter 'rom-*.txt' | Get-Content)
    if ($romSelections.Count -ne 2 -or $romSelections[0] -eq $romSelections[1]) { throw 'ROM selection leaked between prepared run profiles.' }
    Write-Host 'Single-file player verified: clean startup, prepared invitation, isolated ROM choices, and real mGBA read, empty-party startup, dock, detach and PC item checks for German Ruby.'
} finally {
    $directory = Get-Item -LiteralPath $testRoot
    if ($directory.FullName -eq $testRoot -and $directory.Name -match '^soullink-player-check-[a-f0-9]{32}$' -and
        -not ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -and
        -not (Get-ChildItem -LiteralPath $testRoot -Recurse -Force -Attributes ReparsePoint)) {
        Remove-Item -LiteralPath $directory.FullName -Recurse -Force
    }
}
