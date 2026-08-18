# Run-Datenversionen und Migrationen

`RunStore` persistiert Runs als versioniertes Root-Dokument:

```json
{
  "SchemaVersion": 1,
  "Runs": []
}
```

Die aktuelle Version steht in `RunStoreMigrationPipeline.CurrentSchemaVersion`. Neue Dateien werden immer mit dieser Version geschrieben.

## Abwaertskompatibilitaet

Dateien aus der Zeit vor Schema-Version 1 bestanden aus einem nackten JSON-Array:

```json
[
  {
    "Id": "00000000-0000-0000-0000-000000000000",
    "GuildId": "guild-1"
  }
]
```

`RunStore.DeserializeDocument` erkennt dieses Format, ordnet ihm intern Version 0 zu und uebergibt es an die Migrations-Pipeline. Beim naechsten Speichern wird das aktuelle versionierte Format geschrieben. Die bestehende Backup-Datei bleibt dabei Teil des atomaren Speicherablaufs.

## Eine neue Version hinzufuegen

1. Das neue persistierte Modell abwaertskompatibel deserialisierbar gestalten oder ein separates Zwischenmodell einfuehren.
2. Eine Implementierung von `IRunStoreMigration` unter `PokeSoulLinkBot/Persistence/Migrations` anlegen.
3. `SourceVersion` und `TargetVersion` als genau einen aufeinanderfolgenden Schritt definieren.
4. Die Migration in `RunStoreMigrationPipeline` registrieren.
5. `CurrentSchemaVersion` erst danach auf die neue Zielversion erhoehen.
6. Tests fuer das bisherige Format, den Migrationsschritt und das aktuelle Ausgabeformat ergaenzen.

Migrationen duerfen keine Guilds, Runs oder fachlichen Eintraege stillschweigend verwerfen. Eine unbekannte, negative oder neuere Version wird abgelehnt, damit eine aeltere Bot-Version keine Daten mit einem nicht verstandenen Schema ueberschreibt.
