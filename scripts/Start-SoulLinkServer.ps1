param([string]$ListenUrl = 'http://127.0.0.1:5080', [switch]$WithoutDiscord, [string]$DatabasePath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $DatabasePath) { $DatabasePath = Join-Path $root 'artifacts\automation-test\soullink.sqlite' }
$testDatabasePath = [IO.Path]::GetFullPath($DatabasePath)
Push-Location (Join-Path $root 'PokeSoulLinkBot')
try {
    $serverArguments = @("--SoulLink:ListenUrl=$ListenUrl")
    if ($WithoutDiscord) { $serverArguments += '--SoulLink:EnableDiscordEvents=false' }
    $serverArguments += "--SoulLink:DatabasePath=$testDatabasePath"
    Write-Host 'Automations-Test: bestehende Run-JSON ist schreibgeschützt.'
    Write-Host "Testdatenbank: $testDatabasePath"
    dotnet run --project PokeSoulLinkBot.csproj -- @serverArguments
    if ($LASTEXITCODE -ne 0) { throw 'Server failed.' }
} finally {
    Pop-Location
}
