#requires -Version 7.0
param([string]$RuntimeVersion = '10.0.15', [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/server' }
$directory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($directory) | Out-Null
$stage = Join-Path $directory ('.server-publish-' + [Guid]::NewGuid().ToString('N'))
$archive = Join-Path $directory 'soullink-linux-x64.tar.gz'
$temporaryArchive = $stage + '.tar.gz'
try {
    dotnet publish (Join-Path $root 'PokeSoulLinkBot/PokeSoulLinkBot.csproj') -c Release -r linux-x64 --self-contained true -o $stage "-p:ServerRuntimeVersion=$RuntimeVersion"
    if ($LASTEXITCODE -ne 0) { throw 'Linux server build failed.' }
    if (Test-Path -LiteralPath (Join-Path $stage 'appsettings.json')) { throw 'Local configuration must stay outside the Linux release.' }
    if (-not (Test-Path -LiteralPath (Join-Path $stage 'PokeSoulLinkBot'))) { throw 'The Linux launcher is missing.' }
    $tar = if ($IsWindows) { 'tar.exe' } else { 'tar' }
    & $tar -czf $temporaryArchive -C $stage .
    if ($LASTEXITCODE -ne 0) { throw 'Linux archive creation failed.' }
    [IO.File]::Move($temporaryArchive, $archive, $true)
    Write-Host "Linux-Serverpaket: $archive"
    Get-FileHash -LiteralPath $archive -Algorithm SHA256
} finally {
    $candidate = Get-Item -LiteralPath $stage -ErrorAction SilentlyContinue
    if ($candidate -and $candidate.Parent.FullName -eq $directory -and $candidate.Name -match '^\.server-publish-[0-9a-f]{32}$' -and
        -not ($candidate.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        Remove-Item -LiteralPath $candidate.FullName -Recurse -Force
    }
    if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive }
}
