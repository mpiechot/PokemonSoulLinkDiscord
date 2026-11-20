#requires -Version 7.0
param(
    [string]$OutputDirectory,
    [string]$Server,
    [string]$Invitation
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts\player' }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$publishPath = Join-Path $outputPath 'build'
$mgbaBundle = & (Join-Path $PSScriptRoot 'Build-SoulLinkMgbaBundle.ps1')
Push-Location $root
try {
    dotnet publish SoulLink.Player/SoulLink.Player.csproj -c Release -r win-x64 --self-contained true -o $publishPath -m:1 `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -p:UseSharedCompilation=false "-p:MgbaBundlePath=$mgbaBundle"
    if ($LASTEXITCODE -ne 0) { throw 'Windows player build failed.' }
    $source = Join-Path $publishPath 'SoulLink.Player.exe'
    $destination = Join-Path $outputPath 'SoulLink.exe'
    if ($Server -or $Invitation) {
        if (-not ($Server -and $Invitation)) { throw 'Provide Server and Invitation together.' }
        # The published app writes a copy with only public join details, never stored bearer tokens.
        $startInfo = [Diagnostics.ProcessStartInfo]::new($source)
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        foreach ($argument in @('--prepare-player', $destination, $Server, $Invitation)) { $startInfo.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($startInfo)
        $process.WaitForExit()
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $destination)) { throw 'Player file creation failed. Choose a fresh OutputDirectory.' }
    } else {
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
    Write-Host "Spielerdatei: $destination"
    Write-Host 'Nur diese EXE weitergeben. Keine .NET-Installation, kein Terminal und kein Browser nötig.'
} finally {
    Pop-Location
}
