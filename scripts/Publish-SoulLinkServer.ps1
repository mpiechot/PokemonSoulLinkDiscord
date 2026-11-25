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

function Copy-LinuxText([string]$Source, [string]$Destination) {
    $content = [IO.File]::ReadAllText($Source).Replace("`r`n", "`n")
    [IO.File]::WriteAllText($Destination, $content)
}

try {
    dotnet publish (Join-Path $root 'PokeSoulLinkBot/PokeSoulLinkBot.csproj') -c Release -r linux-x64 --self-contained true -o $stage "-p:ServerRuntimeVersion=$RuntimeVersion"
    if ($LASTEXITCODE -ne 0) { throw 'Linux server build failed.' }
    if (Get-ChildItem -LiteralPath $stage -File -Filter 'appsettings*.json') { throw 'Local configuration must stay outside the Linux release.' }
    $data = Join-Path $stage 'Data'
    if (-not (Test-Path -LiteralPath (Join-Path $data 'game-data-fallback.json'))) { throw 'The game data catalog is missing.' }
    if (Get-ChildItem -LiteralPath $data -File | Where-Object Name -ne 'game-data-fallback.json') {
        throw 'Local data must stay outside the Linux release.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $stage 'PokeSoulLinkBot'))) { throw 'The Linux launcher is missing.' }
    Copy-LinuxText (Join-Path $PSScriptRoot 'Install-SoulLinkPlayerRelease.py') (Join-Path $stage 'Install-SoulLinkPlayerRelease.py')
    $deployment = Join-Path $stage 'deploy'
    [IO.Directory]::CreateDirectory($deployment) | Out-Null
    foreach ($file in 'soullink.service', 'Update-SoulLinkServer.sh') {
        Copy-LinuxText (Join-Path $PSScriptRoot $file) (Join-Path $deployment $file)
    }
    foreach ($file in 'server.env.example', 'Caddyfile.example') {
        Copy-LinuxText (Join-Path $root "deploy/$file") (Join-Path $deployment $file)
    }
    Copy-LinuxText (Join-Path $root 'docs/soullink/self-hosting.md') (Join-Path $deployment 'SELF-HOSTING.md')
    & (Join-Path $PSScriptRoot 'Copy-SoulLinkReleaseNotices.ps1') -AssetsFile (Join-Path $root 'PokeSoulLinkBot/obj/project.assets.json') `
        -RuntimeIdentifier linux-x64 -Destination $stage
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
