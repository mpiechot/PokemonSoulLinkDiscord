# Bot bauen und veroeffentlichen

Der Bot wird als framework-abhaengige .NET-9-Konsolenanwendung veroeffentlicht. Der Publish-Output liegt bewusst unter `artifacts/publish/PokeSoulLinkBot` und damit ausserhalb von `bin/Debug`, `bin/Release` und `obj`. Visual Studio und laufende Debug-Builds werden dadurch nicht blockiert.

## Voraussetzungen

- .NET SDK 9 oder neuer
- PowerShell 7 oder Windows PowerShell 5.1
- Zugriff auf NuGet fuer einen erstmaligen Restore

## Empfohlener Publish-Build

Im Repository-Root:

```powershell
./scripts/publish-bot.ps1
```

Das Script:

- prueft die installierte .NET-SDK-Version;
- verhindert einen Output unter `PokeSoulLinkBot/bin` oder `PokeSoulLinkBot/obj`;
- erstellt einen Release-Publish unter `artifacts/publish/PokeSoulLinkBot`;
- veroeffentlicht framework-abhaengig und ohne plattformspezifischen AppHost;
- prueft die erzeugte DLL, `.deps.json`, `.runtimeconfig.json` sowie alle im Projekt vorhandenen Dateien unter `Data` und `Resources`.

Wenn der Restore bereits ausgefuehrt wurde:

```powershell
./scripts/publish-bot.ps1 -NoRestore
```

Ein alternatives Ausgabeverzeichnis kann explizit angegeben werden:

```powershell
./scripts/publish-bot.ps1 -OutputDirectory "C:\Deploy\PokeSoulLinkBot"
```

Der entsprechende direkte .NET-Befehl lautet:

```powershell
dotnet publish PokeSoulLinkBot/PokeSoulLinkBot.csproj `
  --configuration Release `
  --output artifacts/publish/PokeSoulLinkBot `
  --no-self-contained `
  -p:UseAppHost=false
```

## Konfiguration

Secrets werden nicht in den Publish-Output kopiert. Vor dem Start muss mindestens `DISCORD_BOT_TOKEN` als Umgebungsvariable oder im lokalen User-Secrets-Store des Projekts vorhanden sein.

Optionale Umgebungsvariablen:

- `DISCORD_COMMAND_REGISTRATION_MODE`: `all` (Standard), `global`, `guild`, `guilds` oder `development`
- `DISCORD_COMMAND_GUILD_IDS`: mit Komma, Semikolon oder Leerzeichen getrennte Guild-IDs; leer bedeutet alle verbundenen Guilds

Die vom SDK erzeugten Dateien `PokeSoulLinkBot.runtimeconfig.json` und `PokeSoulLinkBot.deps.json` enthalten die technische Laufzeitkonfiguration. Bilder unter `Resources` und der Offline-Katalog unter `Data/game-data-fallback.json` werden durch das Projekt mitveroeffentlicht.

## Bot starten

PowerShell:

```powershell
$env:DISCORD_BOT_TOKEN = "<token>"
dotnet artifacts/publish/PokeSoulLinkBot/PokeSoulLinkBot.dll
```

Linux/macOS:

```bash
export DISCORD_BOT_TOKEN="<token>"
dotnet artifacts/publish/PokeSoulLinkBot/PokeSoulLinkBot.dll
```

Der Prozess bleibt im Vordergrund aktiv und schreibt strukturierte Logs in die Konsole. Beenden erfolgt mit `Ctrl+C` oder ueber den Prozessmanager des verwendeten Hosting-Systems.

## Publish-Output pruefen

Mindestens folgende Dateien und Verzeichnisse muessen vorhanden sein:

```text
artifacts/publish/PokeSoulLinkBot/
|-- PokeSoulLinkBot.dll
|-- PokeSoulLinkBot.deps.json
|-- PokeSoulLinkBot.runtimeconfig.json
|-- Data/
|   `-- game-data-fallback.json
`-- Resources/
    |-- run-start.png
    `-- status.png
```

Weitere abhaengige Assemblies und Ressourcen werden von `dotnet publish` automatisch ergaenzt.
