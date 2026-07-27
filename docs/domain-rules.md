# Domain-Regeln fuer Soul-Link-Runs

Dieses Dokument beschreibt die aktuell implementierten fachlichen Invarianten. Es ist die Referenz fuer neue Commands und Service-Aenderungen. Regeln unter **Offene Fragen** sind noch nicht abschliessend entschieden und duerfen nicht stillschweigend als neue Invariante implementiert werden.

## Begriffe

- **Run**: Eine Soul-Link-Spielrunde in einer Discord-Guild mit Edition, Spielern und Verlauf.
- **Aktiver Run**: Der Run-Kontext, auf den Commands einer Guild ohne weiteren Run-Parameter zugreifen.
- **Route**: Eine normalisierte Route oder Area innerhalb eines Runs.
- **Link-Gruppe**: Alle auf derselben Route gefangenen Pokemon der teilnehmenden Spieler.
- **Team**: Bis zu sechs aktive Link-Gruppen in festen Positionen.
- **Box**: Jede lebende Link-Gruppe, die aktuell nicht im Team referenziert wird.
- **Tod**: Das gemeinsame Ausscheiden aller Pokemon einer Link-Gruppe.
- **Verlorene Route**: Eine Route, auf der kein erstes Encounter gefangen wurde.

Die massgeblichen Modelle sind `SoulLinkRun`, `LinkGroup`, `LinkedPokemon` und `RunPlayer` unter `PokeSoulLinkBot/Core/Models`. Zustandsaenderungen gehoeren in `PokeSoulLinkBot/Application/Services/RunService.cs`, nicht direkt in Discord-Commands.

## Run

### Invarianten

- Ein Run besitzt eine nichtleere Guild-ID, einen Namen, eine Edition und mindestens einen Spieler.
- `/run-start` verlangt aktuell genau drei Discord-User; `RunService.StartRun` akzeptiert technisch mindestens einen Spieler.
- Pro Guild gibt es genau einen aktiven Run-Kontext. Alle Commands ohne Run-Parameter arbeiten auf diesem Kontext.
- Historische, beendete Runs bleiben fuer Statistiken gespeichert.
- Jede fachliche Aenderung wird ueber `IRunStore.Save` beziehungsweise `IRunStore.AddRun` persistiert.

### Erlaubte Uebergaenge

| Von | Aktion | Nach |
| --- | --- | --- |
| kein aktiver Run | Run starten | aktiver Run mit Startzeit |
| aktiver Run | Run beenden | beendeter historischer Run |

### Verbotene Uebergaenge

- Ein neuer Run darf nicht gestartet werden, solange der betroffene aktive Kontext noch nicht beendet ist.
- Catch, Route-Verlust, Team-Aenderung, Swap, Tod und Arena-Fortschritt sind ohne aktiven Run nicht erlaubt.
- Ein beendeter Run kann derzeit nicht wieder geoeffnet oder veraendert werden.

`RunService.EndRun` setzt `EndedAtUtc` und einen bereinigten Endgrund. Bei leerem Grund wird `No reason given.` gespeichert.

## Route und Link-Gruppe

### Invarianten

- Routen werden mit `Trim` und `ToLowerInvariant` normalisiert.
- Innerhalb eines Runs repraesentiert eine Route genau eine Link-Gruppe.
- Ein Spieler darf pro Link-Gruppe hoechstens einen Catch besitzen.
- Nur Spieler des aktiven Runs duerfen einen Catch oder einen verursachenden Spieler-Eintrag erhalten.
- Ein Catch speichert Pokemon-Name, Typen, Spieler, Fangzeit und den initialen Zustand `IsAlive = true`.
- `LinkGroup.IsAlive` ist abgeleitet: Eine Link-Gruppe lebt, wenn mindestens ein Eintrag lebt.

### Erlaubte Uebergaenge

| Von | Aktion | Nach |
| --- | --- | --- |
| unbekannte Route | erster Catch | neue Link-Gruppe mit einem Eintrag |
| Link-Gruppe ohne Eintrag dieses Spielers | weiterer Catch | gleicher Link-Gruppe wird ein Eintrag hinzugefuegt |
| unbekannte Route ohne Catch | `/route-death` | verlorene Link-Gruppe ohne Pokemon |

### Verbotene Uebergaenge

- Derselbe Spieler darf auf derselben Route keinen zweiten Catch registrieren.
- Ein User ausserhalb des Runs darf keinen Catch registrieren.
- Eine Route mit mindestens einem Catch darf nicht nachtraeglich ueber `/route-death` als Encounter-Verlust markiert werden; dafuer ist `/death` vorgesehen.
- Eine bereits als verloren markierte Route darf nicht erneut als verloren markiert werden.

## Team und Box

### Invarianten

- `SoulLinkRun.ActiveLinks` besitzt sechs feste, nullbasierte Slots; Commands verwenden die Positionen 1 bis 6.
- Der erste Catch einer neuen Link-Gruppe versucht, die erste freie Teamposition zu belegen.
- Ist das Team voll, bleibt die neue lebende Link-Gruppe automatisch in der Box.
- Die Box ist kein eigener persistierter Container. Sie wird aus lebenden Link-Gruppen abgeleitet, die nicht im Team referenziert sind.
- Tote oder verlorene Link-Gruppen gelten nicht als Box-Eintraege.

### Erlaubte Uebergaenge

| Von | Aktion | Nach |
| --- | --- | --- |
| neue lebende Link-Gruppe, Team nicht voll | Catch | erste freie Teamposition |
| neue lebende Link-Gruppe, Team voll | Catch | Box |
| lebende Team- oder Box-Gruppe | `/use <route> <position>` | Gruppe an der gewaehlten Teamposition |
| Team-Gruppe plus Box-Gruppe | `/swap` | Box-Gruppe ersetzt Team-Gruppe am selben Slot |

`/use` ersetzt die vorherige Gruppe an der Zielposition. Die ersetzte lebende Gruppe bleibt im Run und wird dadurch zur Box-Gruppe.

### Verbotene Uebergaenge

- Teampositionen ausserhalb 1 bis 6 sind ungueltig.
- Eine tote oder verlorene Gruppe darf nicht mit `/use` aktiviert oder als Box-Seite eines Swaps verwendet werden.
- Die Team-Seite eines Swaps muss aktuell in `ActiveLinks` referenziert sein.
- Die Box-Seite eines Swaps darf nicht bereits im Team liegen.

## Tod und verlorene Route

### Invarianten

- `/death` bezieht sich auf eine existierende Link-Gruppe und verlangt einen nichtleeren Grund.
- Der Tod eines einzelnen Pokemon propagiert auf alle Eintraege der Link-Gruppe.
- Fuer jeden Eintrag werden `IsAlive = false`, Todeszeit, Grund und optional der verursachende Spieler gespeichert.
- Ein optional angegebener verursachender Spieler muss am Run teilnehmen.
- Eine verlorene Route besitzt keine Pokemon-Eintraege, speichert Grund, Zeitpunkt und optional den Spieler des fehlgeschlagenen Encounters.
- Wird kein Verlustgrund angegeben, gilt `First encounter was not caught.`.
- Eine verlorene Route wird aus allen Teampositionen entfernt.
- Tote Gruppen bleiben technisch in `ActiveLinks`, werden aber von Team- und Statusdarstellungen ueber `IsAlive` ausgefiltert und koennen nicht mit `/use` aktiviert werden.

### Verbotene Uebergaenge

- `/death` darf keine unbekannte Route erzeugen.
- `/route-death` darf keine Route mit bereits registrierten Pokemon ueberschreiben.
- Ein nicht am Run beteiligter User darf nicht als verursachender Spieler gespeichert werden.

## Swap

Ein Swap ist atomar aus Sicht von `RunService`: Beide Routen werden validiert, anschliessend wird genau der gefundene Team-Slot ersetzt und der Store einmal gespeichert.

Erlaubt ist:

```text
lebende Team-Gruppe + lebende Box-Gruppe -> Box-Gruppe im bisherigen Team-Slot
```

Verboten ist:

```text
unbekannte Team-Route
bereits aktive Box-Route
tote oder verlorene Box-Route
```

Die ausgewechselte lebende Team-Gruppe bleibt in `LinkGroups` und ist danach automatisch Teil der abgeleiteten Box.

## Run-Ende

- Nur der aktive Run kann beendet werden.
- Das Ende setzt eine UTC-Zeit und einen Endgrund.
- Nach dem Ende duerfen aktive Commands den Run nicht mehr veraendern.
- Link-Gruppen, Team, Box, Tode und Arena-Fortschritt bleiben als Historie erhalten.
- Es gibt derzeit keinen fachlichen Uebergang von `beendet` zurueck zu `aktiv`.

## Relevante Tests

Die zentrale Spezifikation in Tests ist `PokeSoulLinkBot.Tests/RunServiceCatchTests.cs`:

- `StartRun_Should...` prueft Startbedingungen und Guild-Trennung.
- `RegisterCatch_Should...` prueft Route, Spieler, Link-Gruppen und Teamaufnahme.
- `MarkRouteLost_Should...` prueft verlorene Routen und Team-Entfernung.
- `RegisterDeath_Should...` prueft die Todespropagation.
- `TryAddToActive_Should...` prueft Teamkapazitaet und automatische Box-Zuordnung.
- `UseRoute_Should...` und `SwapRoute_Should...` pruefen Team-/Box-Uebergaenge.

Weitere relevante Spezifikationen:

- `PokeSoulLinkBot.Tests/RunStoreTests.cs`: Persistenz, Backup und paralleles Speichern
- `PokeSoulLinkBot.Tests/CatchEligibilityServiceTests.cs`: Art- und Entwicklungsreihen-Sperren
- `PokeSoulLinkBot.Tests/EmbedFactoryStatusTests.cs`: sichtbare Einordnung in Team, Box und Tod

## Offene Fragen

Diese Punkte sind im aktuellen Modell nicht eindeutig oder noch nicht vollstaendig erzwungen:

1. **Verlorene Route als terminaler Zustand:** `MarkRouteLost` verhindert einen zweiten Verlust, `RegisterCatch` blockiert einen spaeteren Catch auf derselben verlorenen Route derzeit jedoch nicht. Soll `verloren -> Catch` immer verboten sein?
2. **Teamaufnahme erst bei vollstaendigem Link:** Der erste Catch einer Route nimmt die noch unvollstaendige Link-Gruppe sofort ins Team. Soll das erst passieren, wenn alle Run-Spieler ihren Catch eingetragen haben?
3. **Doppelte Teampositionen:** `TryAddToActive` verhindert Duplikate, `UseRoute` kann dieselbe Route derzeit aber an eine weitere Position setzen. Soll jede Link-Gruppe hoechstens einmal im Team vorkommen?
4. **Tod im Team:** Tote Gruppen werden in Darstellungen ausgefiltert, bleiben aber als Referenz in `ActiveLinks`. Soll der Tod die Slots explizit leeren?
5. **Wiederholter Tod:** Ein erneuter `/death`-Aufruf ueberschreibt Todeszeit, Grund und verursachenden Spieler. Soll dieser Uebergang abgelehnt oder idempotent behandelt werden?
6. **Spieleranzahl:** Der Command verlangt drei Spieler, der Service nur mindestens einen. Welche Spieleranzahlen sind fachlich zulaessig?
7. **Run-Ende:** Gibt es neben dem manuellen Ende kuenftig fachliche Endzustaende wie Sieg, Aufgabe oder Regelverstoss, und sollen sie strukturiert statt als Freitext gespeichert werden?
