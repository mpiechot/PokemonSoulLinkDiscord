param([string]$ListenUrl = 'http://127.0.0.1:5080', [switch]$WithoutDiscord, [string]$DatabasePath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location (Join-Path $root 'PokeSoulLinkBot')
try {
    $serverArguments = @("--SoulLink:ListenUrl=$ListenUrl")
    if ($WithoutDiscord) { $serverArguments += '--SoulLink:EnableDiscordEvents=false' }
    if ($DatabasePath) { $serverArguments += "--SoulLink:DatabasePath=$([IO.Path]::GetFullPath($DatabasePath))" }
    dotnet run --project PokeSoulLinkBot.csproj -- @serverArguments
    if ($LASTEXITCODE -ne 0) { throw 'Server failed.' }
} finally {
    Pop-Location
}
