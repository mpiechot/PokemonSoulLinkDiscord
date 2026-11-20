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
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$publishPath = [IO.Path]::GetFullPath((Join-Path $tempRoot ('soullink-publish-' + [Guid]::NewGuid().ToString('N'))))
$mgbaBundle = & (Join-Path $PSScriptRoot 'Build-SoulLinkMgbaBundle.ps1')
Push-Location $root
try {
    dotnet publish SoulLink.Player/SoulLink.Player.csproj -c Release -r win-x64 --self-contained true -o $publishPath -m:1 `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -p:UseSharedCompilation=false "-p:MgbaBundlePath=$mgbaBundle"
    if ($LASTEXITCODE -ne 0) { throw 'Windows player build failed.' }
    $source = Join-Path $publishPath 'SoulLink.Player.exe'
    $destination = Join-Path $outputPath 'SoulLink.exe'
    [IO.Directory]::CreateDirectory($outputPath) | Out-Null
    if ($Server -or $Invitation) {
        if (-not ($Server -and $Invitation)) { throw 'Provide Server and Invitation together.' }
        # The published app writes a copy with only public join details, never stored bearer tokens.
        $prepared = Join-Path $publishPath 'Prepared.exe'
        $startInfo = [Diagnostics.ProcessStartInfo]::new($source)
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        foreach ($argument in @('--prepare-player', $prepared, $Server, $Invitation)) { $startInfo.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($startInfo)
        try {
            $process.WaitForExit()
            if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $prepared)) { throw 'Player file creation failed.' }
        } finally {
            $process.Dispose()
        }
        $source = $prepared
    }
    Copy-Item -LiteralPath $source -Destination $destination -Force
    Write-Host "Spielerdatei: $destination"
    Write-Host 'Nur diese EXE weitergeben. Keine .NET-Installation, kein Terminal und kein Browser nötig.'
} finally {
    Pop-Location
    if (Test-Path -LiteralPath $publishPath -PathType Container) {
        $directory = Get-Item -LiteralPath $publishPath
        if ($directory.FullName -eq $publishPath -and $directory.Name -match '^soullink-publish-[a-f0-9]{32}$' -and
            -not ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -and
            -not (Get-ChildItem -LiteralPath $publishPath -Recurse -Force -Attributes ReparsePoint)) {
            Remove-Item -LiteralPath $directory.FullName -Recurse -Force
        }
    }
}
