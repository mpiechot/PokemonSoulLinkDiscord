param([int]$UiPort = 5090, [int]$BridgePort = 5091, [string]$SettingsPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location (Join-Path $root 'SoulLink.Client')
try {
    $clientArguments = @("--UiPort=$UiPort", "--BridgePort=$BridgePort")
    if ($SettingsPath) { $clientArguments += "--SettingsPath=$([IO.Path]::GetFullPath($SettingsPath))" }
    Write-Host "Client UI: http://127.0.0.1:$UiPort"
    dotnet run --project SoulLink.Client.csproj -- @clientArguments
    if ($LASTEXITCODE -ne 0) { throw 'Client failed.' }
} finally {
    Pop-Location
}
