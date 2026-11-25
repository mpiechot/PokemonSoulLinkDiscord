#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$directory = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/emulator'))
$null = New-Item -ItemType Directory -Path $directory -Force

# The official portable x64 build includes the Qt --script option and downstream notices.
$archive = Join-Path $directory 'mGBA-build-2026-11-20-win64-9187.7z'
$uri = 'https://s3.amazonaws.com/mgba/build/mGBA-build-2026-11-20-win64-9187-e6c17152dec0caaebd8a4b949c96a24d56debf44.7z'
$sha256 = 'F4FBE69E9F4E559E67F724BA67F4D2625FBCBDB66A51A217BDCEAE5BBFF57C66'
if (-not (Test-Path -LiteralPath $archive)) {
    Write-Host 'Lade den portablen mGBA-Build für das Spielerpaket …'
    Invoke-WebRequest -Uri $uri -OutFile "$archive.download" -TimeoutSec 60
    Move-Item -LiteralPath "$archive.download" -Destination $archive
}
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash -ne $sha256) {
    throw "SHA-256 mismatch for $archive. Remove that file and run the build again."
}

$stage = Join-Path $directory ('stage-' + [Guid]::NewGuid().ToString('N'))
$zip = Join-Path $directory 'mGBA-9187.zip'
$temporaryZip = "$stage.zip"
$null = New-Item -ItemType Directory -Path $stage
try {
    & tar.exe -xf $archive -C $stage --strip-components 1
    if ($LASTEXITCODE -ne 0) { throw 'Failed to extract the verified mGBA archive.' }
    foreach ($required in 'mGBA.exe', 'portable.ini', 'LICENSE.txt') {
        if (-not (Test-Path -LiteralPath (Join-Path $stage $required))) { throw "Missing mGBA package file: $required" }
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip)
    [IO.File]::Move($temporaryZip, $zip, $true)
} finally {
    $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
    if ([IO.Path]::GetDirectoryName($resolvedStage) -eq $directory -and (Split-Path -Leaf $resolvedStage) -like 'stage-*') {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
    if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip -Force }
}
$zip
