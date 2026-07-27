# Game-Data-Fallback aktualisieren

Der gebuendelte Katalog `PokeSoulLinkBot/Data/game-data-fallback.json` wird mit dem .NET-Tool unter `tools/GameDataCatalogGenerator` aus PokeAPI-Daten erzeugt.

## Wann aktualisieren?

- wenn PokeAPI neue Editionen oder Location Areas bereitstellt;
- wenn Editions- oder Routen-Autocomplete sichtbar veraltet ist;
- vor einer geplanten Bot-Version, die den Fallback-Katalog inhaltlich erweitert;
- nach einer Aenderung der `GameDataCatalog`-Schema-Version.

## Katalog generieren

Im Repository-Root:

```powershell
dotnet run --project tools/GameDataCatalogGenerator/GameDataCatalogGenerator.csproj -- `
  generate `
  --output PokeSoulLinkBot/Data/game-data-fallback.json
```

Der Generator:

1. laedt Editionen und Location Areas ueber den vorhandenen PokeAPI-Katalogservice;
2. entfernt Editionen ohne Encounter-Routen;
3. normalisiert und dedupliziert Editionen und Routen;
4. sortiert Editionen und Routen kulturunabhaengig und stabil;
5. setzt `schemaVersion` und `refreshedAtUtc`;
6. validiert Schema, Zeitstempel, Mindestanzahl, Vollstaendigkeit und Eindeutigkeit;
7. ersetzt die Zieldatei erst nach erfolgreicher Validierung atomar.

`refreshedAtUtc` verwendet standardmaessig die aktuelle UTC-Zeit. Fuer byte-identische Reproduktionen kann der Zeitstempel explizit gesetzt werden:

```powershell
dotnet run --project tools/GameDataCatalogGenerator/GameDataCatalogGenerator.csproj -- `
  generate `
  --output artifacts/generated/game-data-fallback.json `
  --refreshed-at-utc "2026-07-27T12:00:00Z"
```

Bei gleichem PokeAPI-Datenstand und gleichem Zeitstempel ist das erzeugte JSON byte-identisch.

## Vor dem Commit validieren

Die Validierung benoetigt keinen Netzwerkzugriff:

```powershell
dotnet run --project tools/GameDataCatalogGenerator/GameDataCatalogGenerator.csproj -- `
  validate `
  --input PokeSoulLinkBot/Data/game-data-fallback.json
```

Ein Exitcode ungleich 0 markiert leere, inkompatible, doppelte oder offensichtlich unvollstaendige Ausgaben. Die deterministische Sortierung wird vom Generator selbst hergestellt und durch Tests abgesichert. Danach den Diff bewusst auf unerwartet entfernte Editionen oder Routen pruefen:

```powershell
git diff -- PokeSoulLinkBot/Data/game-data-fallback.json
```

Die Generator- und Validator-Tests liegen in `PokeSoulLinkBot.Tests/GameDataFallbackCatalogToolTests.cs`.
