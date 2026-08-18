# Kommando-Backlog

Diese Datei sammelt bewusst zurückgestellte Verbesserungen. Sie enthält keine bereits umgesetzten Commands.

## Hohe Priorität

### `/route`
Detailansicht einer einzelnen Route mit allen Spielern, Pokémon, Status, Teamplatz, Todesgrund und Fangdaten.

### `/box`
Übersicht aller lebenden Link-Gruppen außerhalb des aktiven Teams. Optional mit Filter nach Spieler oder Route.

### `/graveyard`
Übersicht aller verstorbenen Pokémon und Link-Gruppen mit Todesgrund und verantwortlichem Spieler.

### `/help`
Kategorisierte Hilfe mit Beispielen für Run-Verwaltung, Fänge, Team/Box, Arenen und Pokémon-Daten.

### Korrektur- und Audit-Funktionen
Mögliche spätere Ergänzungen:

- `/catch-edit` erweitern, damit auch Route oder Spieler korrigiert werden können
- `/route-reopen` als Alias oder verständlichere Alternative zu `/death-undo`
- `/arena-undo`
- Audit-Log für alle mutierenden Commands
- Bestätigungsbuttons bei `/death`, `/route-death`, `/run-end` und Löschoperationen

## Mittlere Priorität

### `/player-stats`
Statistik für einen einzelnen Spieler: Fänge, lebende Pokémon, Todesfälle, verpasste Begegnungen und aktive Teammitglieder.

### `/history`
Chronologische Übersicht vergangener Runs mit Ergebnis, Endgrund, Fängen, Todesfällen und Arena-Fortschritt.

### `/run-export`
Export des aktuellen oder eines abgeschlossenen Runs als Markdown, JSON oder CSV.

### `/team-check` erweitern
Mögliche Erweiterungen:

- Typ-Schwächen und Resistenzen
- doppelte Typen hervorheben
- fehlende Typen markieren
- zwischen „maximale Typvielfalt“ und „ausgewogene Typabdeckung“ wählen

## Erweiterte Spieldaten

### `/where`
Zeigt, auf welchen Routen ein Pokémon in einer Edition vorkommt.

### `/encounters`
Zeigt alle möglichen Begegnungen auf einer Route und Edition.

### `/random-encounter`
Erzeugt eine zufällige gültige Begegnung unter Berücksichtigung von Duplikatsregel, gesperrten Evolutionslinien und bereits verwendeten Pokémon.

Voraussetzung für diese drei Commands ist eine belastbare Encounter-Datenbasis im Editionskatalog.

## Komfort und Konfiguration

### `/rules`
Zeigt die für den Server oder Run geltenden Soul-Link-Regeln.

### `/settings`
Run- oder serverbezogene Einstellungen, beispielsweise:

- Duplikatsregel
- Shiny-Clause
- Species-Clause
- erlaubte Korrekturrollen
- Standardsprache

### Konsistente Autocomplete- und Lokalisierungsstrategie

- Pokémon-Autocomplete bei `/catch`, `/catch-check`, `/pokedex` und `/moves`
- Routen-Autocomplete bei allen routebezogenen Commands
- deutsche Anzeigenamen mit englischem API-Namen als Fallback
- einheitliche deutsche Rückmeldungen

### PokeAPI-Performance

- persistenter Cache für Attacken, Items und Maschinen
- Request-Deduplizierung für parallele identische Anfragen
- bessere Fehlermeldung bei Rate-Limits oder extern nicht erreichbarer PokéAPI
