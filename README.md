# Pokemon Soul Link Discord Bot

Ein .NET-9-Discord-Bot zum Verwalten laufender Pokemon-Soul-Link-Runs. Der Bot speichert Spieler, Edition, Routen, gefangene und verstorbene Pokemon, Team und Box, Arena-Fortschritt sowie Run-Statistiken pro Discord-Guild.

## Voraussetzungen

- [.NET SDK 9](https://dotnet.microsoft.com/download/dotnet/9.0) oder neuer
- eine Discord Application mit Bot-Token
- eine Discord-Guild, in die die App installiert werden darf
- Internetzugriff auf `https://pokeapi.co` fuer Pokemon-, Editions- und Routendaten

Der Bot verwendet nur den standardmaessig verfuegbaren `Guilds` Gateway Intent. Privilegierte Intents wie Message Content oder Guild Members werden nicht benoetigt. Discord beschreibt die aktuellen Intents in der [Gateway-Dokumentation](https://docs.discord.com/developers/events/gateway).

## Discord-App einrichten

1. Im [Discord Developer Portal](https://discord.com/developers/applications) eine Application erstellen.
2. Unter **Bot** einen Bot-User anlegen und den Token sicher kopieren.
3. Unter den Installations- beziehungsweise OAuth2-Einstellungen eine Guild-Installation mit den Scopes `bot` und `applications.commands` konfigurieren. Discord dokumentiert diese Scopes unter [OAuth2 and Permissions](https://docs.discord.com/developers/platform/oauth2-and-permissions).
4. Nur die benoetigten Bot-Rechte vergeben, insbesondere Nachrichten senden, Links einbetten und Dateien anhaengen.
5. Den erzeugten Installationslink als Guild-Administrator oeffnen und die App zur Test-Guild hinzufuegen.

Den Bot-Token niemals committen, in Logs kopieren oder in einer lokalen JSON-Datei im Repository speichern. Falls ein Token offengelegt wurde, muss er im Developer Portal sofort zurueckgesetzt werden.

## Repository vorbereiten

Im Repository-Root:

```powershell
dotnet restore PokeSoulLinkBot.Tests/PokeSoulLinkBot.Tests.csproj
```

Das Testprojekt referenziert die Bot-Anwendung, daher stellt dieser Restore die Pakete fuer beide Projekte bereit.

## Konfiguration

Die Anwendung liest derzeit keine `appsettings.json`. Konfiguration wird bewusst ueber .NET User Secrets oder Umgebungsvariablen geladen.

| Schluessel | Erforderlich | Standard | Bedeutung |
| --- | --- | --- | --- |
| `DISCORD_BOT_TOKEN` | ja | keiner | geheimer Bot-Token aus dem Discord Developer Portal |
| `DISCORD_COMMAND_REGISTRATION_MODE` | nein | `all` | `all`, `global`, `guild`, `guilds` oder `development`; steuert globale und/oder Guild-spezifische Slash Commands |
| `DISCORD_COMMAND_GUILD_IDS` | nein | alle verbundenen Guilds | mit Komma, Semikolon oder Leerzeichen getrennte Guild-IDs fuer Guild-spezifische Registrierung |

### Empfohlen: User Secrets

```powershell
dotnet user-secrets set "DISCORD_BOT_TOKEN" "<token>" `
  --project PokeSoulLinkBot/PokeSoulLinkBot.csproj

dotnet user-secrets set "DISCORD_COMMAND_REGISTRATION_MODE" "development" `
  --project PokeSoulLinkBot/PokeSoulLinkBot.csproj

dotnet user-secrets set "DISCORD_COMMAND_GUILD_IDS" "<test-guild-id>" `
  --project PokeSoulLinkBot/PokeSoulLinkBot.csproj
```

`development` registriert Commands Guild-spezifisch. Das ist fuer lokale Entwicklung praktisch, weil Guild Commands schneller sichtbar werden als globale Commands.

### Alternative: Umgebungsvariablen

Nur fuer die aktuelle PowerShell-Sitzung:

```powershell
$env:DISCORD_BOT_TOKEN = "<token>"
$env:DISCORD_COMMAND_REGISTRATION_MODE = "development"
$env:DISCORD_COMMAND_GUILD_IDS = "<test-guild-id>"
```

## Bot lokal starten

```powershell
dotnet run --project PokeSoulLinkBot/PokeSoulLinkBot.csproj
```

Nach erfolgreichem Login verbindet sich der Prozess mit Discord, registriert die konfigurierten Slash Commands und bleibt im Vordergrund aktiv. Mit `Ctrl+C` wird er beendet.

Persistente Dateien:

- `PokeSoulLinkBot/bin/<Configuration>/net9.0/Data/runs.json`: Run-Daten beim lokalen `dotnet run`
- `%LOCALAPPDATA%/PokeSoulLinkBot/Data/game-data-catalog.json`: aktualisierter Editions- und Routenkatalog
- `%LOCALAPPDATA%/PokeSoulLinkBot/Data/pokemon-data-cache.json`: persistenter Pokemon-Datencache

Der genaue Pfad von `runs.json` richtet sich nach `AppContext.BaseDirectory`, also nach dem jeweils gestarteten Build- oder Publish-Verzeichnis. Dieses Verzeichnis muss fuer den Bot-Prozess beschreibbar sein.

## Tests und Build

Vollstaendige Testsuite:

```powershell
dotnet test PokeSoulLinkBot.Tests/PokeSoulLinkBot.Tests.csproj
```

Release-Build mit separatem Output:

```powershell
dotnet build PokeSoulLinkBot/PokeSoulLinkBot.csproj `
  --configuration Release `
  --output artifacts/build/PokeSoulLinkBot `
  -p:UseAppHost=false
```

Die CI-Konfiguration unter `.github/workflows/ci.yml` fuehrt Restore, Build und Tests fuer Pull Requests und Pushes auf `main` aus.

## Optionaler Publish-Build

Ein framework-abhaengiger Publish-Output ausserhalb von Visual Studios `bin`-Verzeichnissen:

```powershell
dotnet publish PokeSoulLinkBot/PokeSoulLinkBot.csproj `
  --configuration Release `
  --output artifacts/publish/PokeSoulLinkBot `
  --no-self-contained `
  -p:UseAppHost=false
```

Starten:

```powershell
$env:DISCORD_BOT_TOKEN = "<token>"
dotnet artifacts/publish/PokeSoulLinkBot/PokeSoulLinkBot.dll
```

Der Publish-Output enthaelt die Anwendung, abhaengige Assemblies, Laufzeitkonfiguration, Bilder unter `Resources` und den Offline-Katalog unter `Data/game-data-fallback.json`. Secrets sind absichtlich nicht enthalten.

## Troubleshooting

### `DISCORD_BOT_TOKEN wurde nicht gesetzt`

- User Secrets fuer exakt `PokeSoulLinkBot/PokeSoulLinkBot.csproj` setzen oder die Umgebungsvariable in derselben Shell definieren.
- Keine Anfuehrungszeichen als Teil des gespeicherten Token-Werts verwenden.
- Nach einem Token-Reset den neuen Wert lokal aktualisieren.

### Discord-Login oder Gateway-Verbindung schlaegt fehl

- Token im Developer Portal pruefen beziehungsweise zuruecksetzen.
- Ausgehende HTTPS- und WebSocket-Verbindungen zu Discord in Firewall und Proxy erlauben.
- Systemzeit pruefen; eine stark abweichende Uhr kann TLS- und Authentifizierungsprobleme verursachen.
- Die Konsolenlogs und den Slash Command `/health` auf konkrete Discord-Fehler pruefen.

### Slash Commands sind nicht sichtbar

- Sicherstellen, dass die App mit `applications.commands` in der Guild installiert wurde.
- Fuer lokale Tests `DISCORD_COMMAND_REGISTRATION_MODE=development` und die korrekte Guild-ID setzen.
- Pruefen, ob der Bot Mitglied der Guild ist und dort Nachrichten, Embeds und Attachments senden darf.
- Globale Commands koennen nach einer Aenderung spaeter sichtbar werden als Guild Commands.

### Editionen, Routen oder Pokemon werden nicht geladen

- Erreichbarkeit von `https://pokeapi.co/api/v2/` pruefen.
- Kurzzeitige Timeouts oder Rate Limits spaeter erneut versuchen; der Bot verwendet Retries und lokale Caches.
- Der gebuendelte `Data/game-data-fallback.json` stellt Editions- und Routendaten bereit, wenn noch kein aktueller Cache vorhanden ist.
- Schreibrechte auf `%LOCALAPPDATA%/PokeSoulLinkBot/Data` pruefen.

### Run-Daten werden nicht gespeichert

- Schreibrechte auf das `Data`-Verzeichnis neben der gestarteten Bot-DLL pruefen.
- Sicherstellen, dass nicht zwei Bot-Prozesse denselben Publish-Ordner verwenden.
- `runs.json` und `runs.json.bak` nicht waehrend des laufenden Prozesses manuell bearbeiten.
