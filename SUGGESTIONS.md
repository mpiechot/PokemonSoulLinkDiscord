# Suggestions

Dieses Suggestions-Backlog sammelt Ideen, Folgeaufgaben und Verbesserungen, die waehrend der Arbeit am Projekt auffallen, aber nicht direkt Teil eines aktuellen Tickets sind.

## Offen

### SG-001 README mit Setup- und Betriebsanleitung ausbauen

- Status: offen
- Branch: noch keiner
- Ziel: Die README so erweitern, dass neue Entwickler und Betreiber den Bot lokal konfigurieren, starten und testen koennen.
- Akzeptanzkriterien:
  - Voraussetzungen wie .NET-Version, Discord-Bot-Token und User Secrets sind beschrieben.
  - Lokaler Start, Testausfuehrung und optionaler Publish-Build sind dokumentiert.
  - Wichtige Konfigurationswerte aus `appsettings.json` werden erklaert, ohne Secrets zu dokumentieren.
  - Troubleshooting-Hinweise fuer haeufige Discord- oder API-Probleme sind enthalten.

### SG-002 Slash-Command-Antworten zentral absichern

- Status: offen
- Branch: noch keiner
- Ziel: Eine zentrale Strategie fuer Discord-Interaction-Antworten definieren, damit lang laufende Commands rechtzeitig deferen und konsistent antworten.
- Akzeptanzkriterien:
  - Commands nutzen ein einheitliches Muster fuer `DeferAsync`, Followups und Fehlerantworten.
  - Langsame Datenquellen koennen keinen 3-Sekunden-Interaction-Timeout mehr verursachen.
  - Tests oder gezielte Abdeckung pruefen, dass Router und Commands Antwortpfade korrekt behandeln.
  - Die Loesung passt zu bestehenden Embed- und Fehlerausgaben.

### SG-003 Run-Daten versionieren und migrierbar machen

- Status: offen
- Branch: noch keiner
- Ziel: Persistierte Run-Daten mit einer Version versehen, damit zukuenftige Modellveraenderungen kontrolliert migriert werden koennen.
- Akzeptanzkriterien:
  - Gespeicherte Run-Dateien enthalten eine Schema- oder Datenversion.
  - Bestehende Daten ohne Version werden abwaertskompatibel geladen.
  - Es gibt einen klaren Ort fuer Migrationen zwischen Datenversionen.
  - Tests decken alte und aktuelle Datenformate ab.

### SG-004 Datenservices mit Retry, Timeout und Cache-Policy haerten

- Status: offen
- Branch: noch keiner
- Ziel: Externe Datenzugriffe robuster machen, damit PokeAPI- oder Netzwerkprobleme den Bot nicht unnoetig blockieren.
- Akzeptanzkriterien:
  - HTTP-Zugriffe haben explizite Timeouts und sinnvolle Fehlerbehandlung.
  - Wiederholungen werden begrenzt und nur fuer geeignete Fehler eingesetzt.
  - Cache-Verhalten ist dokumentiert und in Logs nachvollziehbar.
  - Nutzer erhalten klare Meldungen, wenn externe Daten temporaer nicht verfuegbar sind.

### SG-005 Domain-Regeln fuer Soul-Link-Konsistenz dokumentieren

- Status: offen
- Branch: noch keiner
- Ziel: Die wichtigsten fachlichen Regeln fuer Soul-Link-Runs dokumentieren, damit neue Commands dieselben Invarianten beachten.
- Akzeptanzkriterien:
  - Regeln fuer Route, Link-Gruppe, Team, Box, Tod, Swap und Run-Ende sind beschrieben.
  - Erlaubte und verbotene Zustandsuebergaenge sind nachvollziehbar dokumentiert.
  - Die Dokumentation verweist auf relevante Services oder Tests.
  - Offene Regelunsicherheiten sind als Fragen markiert.

### SG-006 Health- oder Diagnose-Command fuer Betreiber ergaenzen

- Status: offen
- Branch: noch keiner
- Ziel: Einen Betreiber-Command bereitstellen, der den Zustand des Bots und wichtiger Datenquellen schnell sichtbar macht.
- Akzeptanzkriterien:
  - Der Command zeigt Discord-Verbindung, aktiven Run, Cache-Status und externe Datenverfuegbarkeit an.
  - Sensible Informationen wie Tokens oder Pfade mit Nutzerdaten werden nicht offengelegt.
  - Ausgabe ist kurz genug fuer Discord und nutzt die vorhandene Ausgabe-Struktur.
  - Fehler werden klar, aber nicht alarmistisch formuliert.

### SG-007 Autocomplete-Qualitaet fuer Pokemon, Editionen und Routen verbessern

- Status: offen
- Branch: noch keiner
- Ziel: Autocomplete-Vorschlaege priorisieren und fehlertoleranter machen, damit Commands im Spielbetrieb schneller bedienbar sind.
- Akzeptanzkriterien:
  - Exakte und haeufig genutzte Treffer werden vor unscharfen Treffern angezeigt.
  - Deutsche und englische Namen funktionieren konsistent.
  - Route- und Edition-Vorschlaege bleiben auch bei partiellen Eingaben stabil.
  - Tests decken typische Suchbegriffe, Tippfehler und leere Eingaben ab.

### SG-008 Command- und Service-Telemetrie vereinheitlichen

- Status: offen
- Branch: noch keiner
- Ziel: Logging so strukturieren, dass Fehler und langsame Pfade im Betrieb leichter nachvollziehbar sind.
- Akzeptanzkriterien:
  - Slash Commands loggen Start, Dauer, Ergebnis und relevante Parameter ohne sensible Daten.
  - Externe Datenzugriffe und Cache-Entscheidungen sind mit konsistenten Event-Namen sichtbar.
  - Fehlerlogs enthalten genug Kontext fuer Debugging, ohne Discord-Nachrichten zu ueberfrachten.
  - Tests oder Review-Checklisten stellen sicher, dass neue Commands das Muster verwenden.
