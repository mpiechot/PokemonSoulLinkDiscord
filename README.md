# PokemonSoulLinkDiscord
This project contains a discord bot for Soul Link runs with Pokemon

## Read-only SoulLink automation

The integration branch adds mGBA → local C# client → SignalR server → SQLite/Discord tracking.
For players, use the single-file Windows companion `SoulLink.exe`: no SDK, terminal, browser or demo data. See the [player guide](docs/soullink/player.md) for host-prepared files and the one-time mGBA bridge setup.

Developers can build it with:

```powershell
.\scripts\Publish-SoulLinkPlayer.ps1
```

The isolated synthetic test harness is separate: `Start-SoulLinkDemo.ps1`. See the [German testing guide](docs/soullink/README.md) for server setup, lifecycle commands and current ROM validation limits. Game memory is always read-only.
