#requires -Version 7.0
param(
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')][string]$Version,
    [Parameter(Mandatory)][string]$Server,
    [string]$KeyFile = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SoulLink\ReleaseSigning\key.dpapi'),
    [string]$PlayerDirectory,
    [string]$Archive,
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]*$')][string]$SshHost
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $PlayerDirectory) { $PlayerDirectory = Join-Path $root 'artifacts/player' }
if (-not $Archive) { $Archive = Join-Path $root 'artifacts/server/soullink-player-release.zip' }
$archivePath = [IO.Path]::GetFullPath($Archive)
$keyPath = [IO.Path]::GetFullPath($KeyFile)
$repoPath = [IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($keyPath.StartsWith($repoPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Keep the Windows account-protected release key outside the repository.'
}
$origin = [uri]$Server
if (-not $origin.IsAbsoluteUri -or $origin.Scheme -ne 'https' -or $origin.AbsolutePath -ne '/' -or $origin.UserInfo -or $origin.Query -or $origin.Fragment) {
    throw 'Use an HTTPS origin for the player update server.'
}
if (([version]$Version).ToString(3) -cne $Version) { throw 'Invalid release version.' }
$player = [IO.Path]::GetFullPath((Join-Path $PlayerDirectory 'SoulLink.exe'))
if (Get-Process -Name SoulLink -ErrorAction SilentlyContinue | Where-Object Path -eq $player) {
    throw 'SoulLink.exe is open. Save in the game and close SoulLink before publishing the EXE.'
}

$key = [Security.Cryptography.RSA]::Create(3072)
$zipTemp = $archivePath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
try {
    $scope = [Security.Cryptography.DataProtectionScope]::CurrentUser
    if ([IO.File]::Exists($keyPath)) {
        $secret = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($keyPath), $null, $scope)
        try { $key.FromXmlString([Text.Encoding]::UTF8.GetString($secret)) } finally { [Array]::Clear($secret) }
    } else {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($keyPath)) | Out-Null
        $secret = [Text.Encoding]::UTF8.GetBytes($key.ToXmlString($true))
        try { $protected = [Security.Cryptography.ProtectedData]::Protect($secret, $null, $scope) } finally { [Array]::Clear($secret) }
        $keyStream = [IO.File]::Open($keyPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $keyStream.Write($protected, 0, $protected.Length) } finally { $keyStream.Dispose() }
    }
    $publicKey = [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
    & (Join-Path $PSScriptRoot 'Publish-SoulLinkPlayer.ps1') -OutputDirectory $PlayerDirectory -UpdateVersion $Version `
        -UpdateServer ($origin.GetLeftPart([UriPartial]::Authority)) -UpdatePublicKey $publicKey
    $sha = (Get-FileHash -LiteralPath $player -Algorithm SHA256).Hash
    $size = (Get-Item -LiteralPath $player).Length
    $sizeText = $size.ToString([Globalization.CultureInfo]::InvariantCulture)
    $data = [Text.Encoding]::UTF8.GetBytes("SoulLinkPlayerUpdate/v1`n$Version`n$sizeText`n$sha`n")
    $signature = [Convert]::ToBase64String($key.SignData($data, [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pss))
    $manifest = @{ format = 1; version = $Version; size = $size; sha256 = $sha; signature = $signature } | ConvertTo-Json -Compress

    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($archivePath)) | Out-Null
    $zip = [IO.Compression.ZipFile]::Open($zipTemp, [IO.Compression.ZipArchiveMode]::Create)
    try {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $player, "SoulLink-$Version.exe") | Out-Null
        $writer = [IO.StreamWriter]::new($zip.CreateEntry('manifest.json').Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write($manifest) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    [IO.File]::Move($zipTemp, $archivePath, $true)
    Write-Host "Signiertes Spielerupdate ${Version}: $archivePath"
    if ($SshHost) {
        $remoteFile = '/tmp/soullink-player-release-' + [guid]::NewGuid().ToString('N') + '.zip'
        & scp $archivePath "${SshHost}:$remoteFile"
        if ($LASTEXITCODE -ne 0) { throw 'Upload of the player release failed.' }
        & ssh $SshHost sudo python3 /opt/soullink/current/Install-SoulLinkPlayerRelease.py $remoteFile
        if ($LASTEXITCODE -ne 0) { throw "Publishing failed. The archive is still available at $remoteFile." }
        & ssh $SshHost rm -f -- $remoteFile
    }
} finally {
    $key.Dispose()
    if ([IO.File]::Exists($zipTemp)) { [IO.File]::Delete($zipTemp) }
}
