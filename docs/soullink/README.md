# SoulLink Automation testen

Dieser Branch ergänzt den Discord-Bot um eine **schreibgeschützte Erfassung** für Pokémon Rubin (deutsche AXVD-ROM). Ein lokaler C#-Client liest mGBA-Lua-Daten, verbindet sich mit dem integrierten SignalR-Server und zeigt Party, Boxbestand, Links und Diagnose an. Im Spiel wird nichts verändert.

## Schnelltest ohne ROM und ohne Discord

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

1. Server in einem Terminal starten. Der bestehende `DISCORD_BOT_TOKEN` bleibt in User Secrets oder der Umgebung:

   ```powershell
   .\scripts\Start-SoulLinkServer.ps1
   ```

2. In Discord `/run-init name:Test edition:ruby` aufrufen. Der Bot liefert einen Einladungscode. Optional `death-rule:zero-hp-outside-battle` wählen; standardmäßig sind automatische Todesmeldungen deaktiviert.
3. Auf jedem Spieler-PC den lokalen Client starten:

   ```powershell
   .\scripts\Start-SoulLinkClient.ps1
   ```

4. [Lokalen Client öffnen](http://127.0.0.1:5090), Serveradresse, Einladung und einen eindeutigen Spielernamen eingeben. Für einen entfernten Server HTTPS verwenden; Loopback erlaubt HTTP. Es werden keine Ports auf den Spieler-PCs freigegeben.
5. mGBA mit einem geladenen Spielstand starten und `integration/mgba/soullink.lua` über **Tools → Scripting → Load script** laden. Der Client muss zuvor laufen.
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
