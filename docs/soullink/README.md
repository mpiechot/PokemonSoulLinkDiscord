# SoulLink Automation testen

Dieser Branch ergänzt den Discord-Bot um eine **schreibgeschützte Erfassung** für Pokémon Rubin (deutsche AXVD-ROM). Ein lokaler C#-Client liest mGBA-Lua-Daten, verbindet sich mit dem integrierten SignalR-Server und zeigt Party, Boxbestand, Links und Diagnose an. Im Spiel wird nichts verändert.

**Für Mitspieler:** Nutze die eigenständige `SoulLink.exe`. Sie startet als Windows-Anwendung ohne Browser, Terminal oder Demodaten. Der Gastgeber kann Server, Einladung und Spielername in deiner Datei vorbereiten. Die kurze [Spieleranleitung](player.md) beschreibt auch die einmalige mGBA-Verbindung.

## Entwicklungsdemo ohne ROM und ohne Discord

Dieser Abschnitt ist ausschließlich für Entwicklung und automatische Tests. Für einen echten Run die Spieler-EXE verwenden. Das Demo ist ausdrücklich mit Testdaten gekennzeichnet und wird nicht vom normalen Client gestartet.

Voraussetzung: .NET 9 SDK oder neuer sowie die .NET-/ASP.NET-Core-9-Runtime **ab 9.0.21**. Im Repository:

```powershell
.\scripts\Start-SoulLinkDemo.ps1
```

Das Demo baut Server und Client, startet beide mit eigenen lokalen Ports und einer neuen Datenbank unter `artifacts/demo-…`, registriert drei simulierte Spieler und öffnet die Diagnoseoberfläche. Es zeigt einen Fang direkt in die Box, eine gemeinsame Link-Gruppe, einen Teamkonflikt und eine Entwicklung. Ctrl+C beendet ausschließlich die Demo-Prozesse. Das Demo verwendet keine echten Spielstände und verbindet sich nicht mit Discord.

Automatische Prüfung, die sich selbst beendet:

```powershell
.\scripts\Start-SoulLinkDemo.ps1 -Verify
```

## Eigener Test mit mGBA und Discord

Für einen parallelen Test zu einem laufenden Run **einen eigenen Discord-Test-Bot mit anderem Token und eine eigene Testdatenbank** verwenden. Zwei Instanzen desselben Bots können dieselben Commands bearbeiten; ein neuer automatischer Run übernimmt die gemeinsamen Status-/Team-Commands in seiner Guild. Der Client allein stellt keine Discord-Verbindung her.

1. Server in einem Terminal starten. Der bestehende `DISCORD_BOT_TOKEN` bleibt in User Secrets oder der Umgebung:

   ```powershell
   .\scripts\Start-SoulLinkServer.ps1
   ```

2. In Discord `/run-init name:Test edition:ruby` aufrufen. Der Bot liefert einen Einladungscode. Optional `death-rule:zero-hp-outside-battle` wählen; standardmäßig sind automatische Todesmeldungen deaktiviert.
3. Die Windows-Spielerdatei veröffentlichen: `./scripts/Publish-SoulLinkPlayer.ps1` (Entwicklerschritt). Die fertige Datei liegt unter `artifacts/player/SoulLink.exe`.
4. Für Mitspieler im Client unter **Einrichtung → Für Gastgeber** eine vorbereitete Datei erstellen. Mitspieler öffnen nur diese EXE; eine allgemeine Datei erlaubt stattdessen die einmalige Anmeldung unter **Run verbinden**. Für entfernte Server HTTPS verwenden. Auf Spieler-PCs wird kein Port extern freigegeben.
5. mGBA mit geladenem Spielstand laufen lassen und die Brücke einmal über **Einrichtung → mGBA verbinden** einrichten. Die [Spieleranleitung](player.md) beschreibt das Laden und den optionalen mGBA-Autostart.
6. **Vor dem Run-Start Party, Boxbestand und Map im Client mit dem Spiel vergleichen.** Die AXVD-Adressen sind ein Ruby-Referenzprofil mit Struktur- und Prüfsummenprüfung, noch kein mit deiner konkreten Randomizer-ROM bestätigtes Profil. Details und Anpassung: [mGBA-Anleitung](mgba.md).
7. Sobald alle Spieler eine gültige Baseline gemeldet haben, in Discord `/run-start` ohne Parameter ausführen. Bereits vorhandene Pokémon werden als Ausgangsbestand importiert; sie erzeugen keine nachträglichen Fangereignisse oder Starter-Links.
8. Pokémon fangen, umbenennen, entwickeln und zwischen Team/Box bewegen. `/status`, `/team` und `/soullink action:status` zeigen den ausgewählten automatischen Run. Neue Identitäten werden als beobachtete Fänge erfasst; alle Spieler im selben Fanggebiet ergeben eine Link-Gruppe.

Die normale manuelle `/run-start`-Variante mit allen Spieleroptionen bleibt verfügbar, wenn kein automatischer Run ausgewählt ist. Bestehende JSON-Runs werden nicht stillschweigend in die neue Domain umgeschrieben; sie bleiben über die bisherigen Commands verfügbar. Bei ausgewähltem automatischem Run blockiert die Kontextgrenze Legacy-Änderungsbefehle. `/stats` zeigt die gespeicherten automatischen Runs, sobald solche vorhanden sind.

## Runs und Korrekturen

| Command | Wirkung |
| --- | --- |
| `/run-init name:…` | Neue Lobby; vorheriger laufender Run wird pausiert. Eine ersetzte ungestartete Lobby wird entfernt. |
| `/run-start` oder `/soullink action:start` | Teilnehmer einfrieren und Erfassung aktivieren. |
| `/show-runs` | Gespeicherte automatische Runs mit IDs, Status und Spielern. |
| `/run-load id:…` | Run auswählen/wiederaufnehmen; Clients anschließend neu verbinden. |
| `/soullink action:pause` | Automatische Verarbeitung pausieren. Wiederaufnahme über `/run-load`. |
| `/run-end` oder `/soullink action:end` | Ausgewählten Run abschließen. |
| `/soullink action:encounter-failed area:…` | Verlorenen Encounter ausdrücklich bestätigen. |
| `/soullink action:death area:…` | Link-Tod bestätigen, ohne das Spiel zu ändern. |
| `/soullink action:death-undo area:…` | Falsche Todesmarkierung aufheben. Bei aktivierter HP-Regel Pokémon zuerst im Spiel heilen. |
| `/soullink action:rebaseline player:…` | Geprüften Spielstand ausdrücklich als neue Baseline zulassen. Anschließend im Client „Neu verbinden“. Keine nachträglichen Fangereignisse. |

Änderungen an automatischen Runs erfordern **Server verwalten**. Spieler-IDs stehen in der lokalen Diagnose; für Rebaseline wird die ID des betroffenen Spielers verwendet. Fanggebiete benutzen Gen-III-Region-IDs, z. B. Route 101 = 16 und Route 102 = 17.

## Bekannte Grenzen des ersten echten Spieltests

- Keine RAM-Schreibzugriffe und keine automatische Team-Anpassung. Gemischte Party-/Boxbelegung erscheint als **TEAM CONFLICT**.
- Ein Erstbestand wird importiert; Starter werden nicht automatisch nachträglich miteinander verlinkt.
- Neue Identität bedeutet „neu beobachtet“. Geschenke, Trades und Fänge lassen sich daraus nicht immer sicher unterscheiden. Die erste gültige Identität je Spieler/Gebiet belegt den Encounter; weitere werden als Extra-Fang diagnostiziert. Shiny-Sonderregeln des separaten manuellen Features werden hier nicht automatisch angewendet.
- Releases, Daycare und Save-State-Rücksprünge können die erwartete Inventarmenge ändern und führen zu **OutOfSync**. Nichts wird automatisch gelöscht oder durch Offline-Ereignisse nachgetragen.
- Die optionale Todesregel verlangt zwei vollständige Beobachtungen mit HP 0 außerhalb eines Kampfes. Die Erkennung ist damit bewusst verzögert; Heilung vor diesen Beobachtungen kann einen Todesfall verbergen. Für verbindliche Run-Regeln den Tod im Bot bestätigen.
- Box-Level werden nicht aus einer möglicherweise randomisierten Wachstumskurve geraten. Box-Pokémon behalten Species, Identität, Erfahrung, Ei-/Shiny-Status und Herkunft.
- Kein echter mGBA-/AXVD-Lauf und keine Discord-Zustellung wurden in dieser Entwicklungsumgebung durchgeführt. Parser, Transport und fachliche Verarbeitung sind mit synthetischen Daten getestet.

Weitere Details: [Architektur](architecture.md), [Protokoll](protocol.md), [Betrieb und Entwicklung](development.md).
