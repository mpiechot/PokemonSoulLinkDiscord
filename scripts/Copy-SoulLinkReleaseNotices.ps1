#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$AssetsFile,
    [Parameter(Mandatory)][string]$RuntimeIdentifier,
    [Parameter(Mandatory)][string]$Destination
)

$ErrorActionPreference = 'Stop'
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json -AsHashtable
$target = $assets.targets.GetEnumerator() | Where-Object { $_.Key.EndsWith("/$RuntimeIdentifier") } | Select-Object -First 1
if (-not $target) { throw "Missing resolved publish target for $RuntimeIdentifier." }

$packages = [Collections.Generic.List[string]]::new()
foreach ($package in $target.Value.GetEnumerator()) {
    if ($package.Value.type -eq 'package' -and ($package.Value.runtime -or $package.Value.native -or $package.Value.runtimeTargets)) {
        $packages.Add($assets.libraries[$package.Key].path)
    }
}
foreach ($framework in $assets.project.frameworks.Values) {
    foreach ($dependency in $framework.downloadDependencies) {
        if ($dependency.name -notlike "*.$RuntimeIdentifier") { continue }
        if ($dependency.version -notmatch '^\[([^,]+), \1\]$') { throw "Unable to resolve $($dependency.name) to a single version." }
        $packages.Add("$($dependency.name.ToLowerInvariant())/$($Matches[1])")
    }
}

$noticeDirectory = [IO.Directory]::CreateDirectory((Join-Path $Destination 'notices')).FullName
foreach ($package in $packages | Sort-Object -Unique) {
    $source = foreach ($folder in $assets.packageFolders.Keys) {
        $candidate = Join-Path $folder $package
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            $candidate
            break
        }
    }
    if (-not $source) { throw "Missing restored package $package." }
    $notices = @(Get-ChildItem -LiteralPath $source -File | Where-Object {
        $_.Name -match '^(LICENSE|LICENCE|NOTICE|THIRD[ -]PARTY[ -]NOTICES?)(\.(txt|md|html|rtf))?$'
    })
    if ($notices.Count -eq 0) { continue }

    $packageDirectory = [IO.Directory]::CreateDirectory((Join-Path $noticeDirectory $package)).FullName
    foreach ($notice in $notices) {
        Copy-Item -LiteralPath $notice.FullName -Destination $packageDirectory -Force
    }
}
$root = Split-Path -Parent $PSScriptRoot
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $Destination -Force