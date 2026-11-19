# PokemonSoulLinkDiscord
This project contains a discord bot for Soul Link runs with Pokemon

## Read-only SoulLink automation

The integration branch adds mGBA → local C# client → SignalR server → SQLite/Discord tracking.
Start an isolated three-player demo without a ROM or Discord connection:

```powershell
.\scripts\Start-SoulLinkDemo.ps1
```

See the [German testing guide](docs/soullink/README.md) for the real-game setup, lifecycle commands and current ROM validation limits. Game memory is always read-only.
