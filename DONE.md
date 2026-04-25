# Done

Diese Datei sammelt erledigte Backlog-Aufgaben, die aus `BACKLOG.md` herausgezogen wurden.

## Erledigt

### BL-002 Einheitliche Command-Ausgaben

- Status: umgesetzt
- Branch: `codex/bl-002-command-ausgaben`
- Ziel: Den Output aller Commands normalisieren, damit Antworten ein einheitliches Bild haben und Daten uebersichtlich angezeigt werden.
- Akzeptanzkriterien:
  - [x] Einheitliche Embed- oder Textstruktur fuer Erfolg, Status, Fehler und Zusammenfassungen.
  - [x] Einheitliche Benennung von Run, Edition, Route, Spieler, Pokemon und Status.
  - [x] Lange Listen bleiben lesbar und Discord-kompatibel.
  - [x] Bestehende Tests werden angepasst oder ergaenzt.

### BL-003 DeathCommand um Reason und verursachenden Spieler erweitern

- Status: umgesetzt
- Branch: `codex/bl-003-death-reason-player`
- Ziel: Der DeathCommand bekommt eine Reason und optional einen Spieler, der Schuld war.
- Akzeptanzkriterien:
  - [x] `reason` ist als Command-Parameter verfuegbar.
  - [x] `player` ist optional als Command-Parameter verfuegbar.
  - [x] Reason und Spieler werden persistiert.
  - [x] Ausgabe zeigt Route, betroffene Pokemon, Reason und optionalen Spieler.

### BL-004 Route direkt als verloren markieren

- Status: erledigt
- Branch: `codex/route-direkt-verloren`
- Ziel: Einen leichten Command fuer Situationen erstellen, in denen eine Route direkt verloren ist, weil das erste Encounter-Pokemon nicht gefangen wurde.
- Vorschlag: `/route-death`
- Akzeptanzkriterien:
  - [x] Route kann direkt als tot/verloren markiert werden, ohne alle Pokemon einzeln zu erfassen.
  - [x] Reason ist verpflichtend oder sinnvoll vorbelegt.
  - [x] Optional kann angegeben werden, welcher Spieler das Encounter nicht gefangen hat.
  - [x] Der Command verhindert widerspruechliche Team-/Box-Zustaende.

### BL-008 Game-Data-Katalog-Fetch beschleunigen und stabilisieren

- Status: erledigt
- Branch: `codex/bl-008-game-data-cache`
- Hintergrund: Der erste Fetch der PokeAPI Location Areas war langsam, weil fuer viele Location Areas einzelne Detail-Requests ausgefuehrt wurden.
- Ziel: Startup und Autocomplete duerfen nicht davon abhaengen, dass alle Location Areas frisch geladen werden.
- Akzeptanzkriterien:
  - [x] Der Cache liegt an einem stabilen Ort und geht nicht bei Clean/Rebuild verloren.
  - [x] Es gibt eine klare Logmeldung, ob Cache, API oder ein noch nicht bereiter Katalog verwendet wird.
  - [x] Location-Area-Details werden begrenzt parallelisiert oder anderweitig schneller geladen.
  - [x] Bei API-Fehlern blockieren Autocomplete und Startup nicht; bestehende Route-Daten aus Runs bleiben nutzbar.
  - [ ] Optional: Ein vorbefuellter JSON-Katalog wird ins Repository aufgenommen.

### BL-009 StyleCop-Dateikopf-Regel deaktivieren

- Status: erledigt
- Branch: `codex/disable-stylecop-file-header`
- Hintergrund: StyleCop meldete `The file header is missing or not located at the top of the file.`
- Ziel: Die File-Header-Warnung wird in der StyleCop-Konfiguration deaktiviert, weil dieses Repository keine verpflichtenden Dateikoepfe verwendet.
- Akzeptanzkriterien:
  - [x] Die betroffene StyleCop-Regel fuer fehlende Dateikoepfe ist zentral deaktiviert.
  - [x] Neue und bestehende Dateien muessen keinen File Header enthalten.
  - [x] StyleCop-Analyse oder Build laeuft ohne diese Warnung durch.
  - [x] Andere StyleCop-Regeln bleiben unveraendert aktiv.
