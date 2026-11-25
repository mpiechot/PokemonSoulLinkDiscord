# SoulLink

SoulLink verbindet einen Windows-Player mit einem selbst gehosteten Run-Server. Der Player liest Spieldaten aus mGBA über eine lokale Lua-Brücke. Der Server synchronisiert Run, Team, Box und Links über SignalR und speichert sie in SQLite. Unterstützt wird derzeit Pokémon Rubin (deutsche AXVD-ROM).

- **`main`** enthält den Windows-Player, den Linux-Run-Server und die optionale Discord-Anbindung.
- **`codex/soullink-discord-bot`** enthält den eigenständigen Discord-Bot für manuell erfasste Runs. Seine README erklärt die Einrichtung mit eigener Discord-Anwendung und eigenem Token.

Die [Spieleranleitung](docs/soullink/player.md) erklärt den Beitritt über eine Einladung. Die [Self-Hosting-Anleitung](docs/soullink/self-hosting.md) beschreibt Installation, HTTPS und Einladungen.

## Windows-Player bauen

Mit dem .NET 10 SDK und PowerShell 7:

```powershell
./scripts/Publish-SoulLinkPlayer.ps1
./scripts/Publish-SoulLinkPlayer.ps1 -PublicBuild
```

Der normale Build schreibt `artifacts/player/SoulLink.exe` mit mitgeliefertem mGBA. Der Build für die Release-Prüfung schreibt `artifacts/player-public/SoulLink.exe`. Er verwendet ein vom Anwender separat bezogenes mGBA und lädt keine externen Spielgrafiken automatisch. Die [Release-Prüfung](docs/soullink/public-release.md) beschreibt den Stand und die offenen Freigabepunkte.

Für Entwicklung und Tests stehen die [lokale Demo und die Diagnose](docs/soullink/README.md) zur Verfügung. Die Demo kann die Datenkette mit synthetischen Spieldaten prüfen.