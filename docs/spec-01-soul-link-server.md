# Technische Spezifikation

## Pokémon Soul-Link Automation für mGBA, C# DiscordBot und zentralen Server

## 1. Ziel des Projekts

Das bestehende C#-DiscordBot-Projekt soll um eine zentrale **Pokémon-Soul-Link-Serverfunktion** erweitert werden.

Mehrere Spieler spielen jeweils lokal Pokémon Rubin über den Emulator **mGBA**.

Auf jedem Spieler-PC läuft zusätzlich ein möglichst kleiner und weitgehend automatisch arbeitender **SoulLink Client**. Dieser kommuniziert lokal mit mGBA und über das Internet mit dem zentralen Server.

Der zentrale Server läuft auf einem bestehenden Linux-/netcup-Server und soll Bestandteil bzw. Modul des bereits vorhandenen C#-DiscordBot-Projekts sein.

Das System soll Soul-Link-Verwaltungsaufwand möglichst vollständig automatisieren.

Langfristiges Ziel:

> Die Spieler sollen einfach Pokémon spielen können. Fänge, Fangorte, Soul-Link-Zuordnungen, Teamänderungen, Boxwechsel, Entwicklungen und Todesfälle werden automatisch erkannt und zwischen den beteiligten Spielen synchronisiert.

Discord dient dabei primär als gemeinsames UI, Log und optional als Administrationsoberfläche.

---

# 2. Grundprinzip

Die Architektur soll aus vier logischen Komponenten bestehen:

```text
┌─────────────────────┐
│ Pokémon Ruby / mGBA │
└─────────┬───────────┘
          │
          │ Lua / mGBA API
          ▼
┌─────────────────────┐
│ Local SoulLink      │
│ Client              │
└─────────┬───────────┘
          │
          │ HTTPS / WebSocket
          ▼
┌─────────────────────┐
│ SoulLink Server     │
│ ASP.NET Core        │
└─────────┬───────────┘
          │
          ├──────────── Discord Bot
          │
          └──────────── Persistenz
```

Für jeden Spieler existieren die oberen beiden Komponenten lokal.

Es gibt genau eine zentrale Serverinstanz pro Deployment.

---

# 3. Bestehendes System

Es existiert bereits ein C#-DiscordBot-Projekt.

Der Soul-Link-Server soll **in dieses bestehende Projekt integriert** werden.

Es soll nach Möglichkeit kein separates, unnötiges Serverprojekt entstehen, sofern die bestehende Architektur eine saubere Integration erlaubt.

Codex soll deshalb zuerst das Repository analysieren und insbesondere feststellen:

- verwendete .NET-Version

- vorhandene Dependency-Injection-Struktur

- vorhandene Hosting-Struktur

- Discord-Bibliothek

- Konfigurationssystem

- Logging

- Persistenz/Datenbank

- Hintergrunddienste

- vorhandene Web-/HTTP-Infrastruktur

- Deploymentstruktur

- Docker/Systemd etc.

- Telegram-/Hermes-bezogene Integrationen, sofern im Repository vorhanden

Bestehende Architekturkonventionen sollen bevorzugt weiterverwendet werden.

---

# 4. Deployment

Der zentrale Dienst läuft auf einem Linux-/netcup-Server.

Auf demselben Server läuft ebenfalls der bestehende DiscordBot.

Zusätzlich läuft dort der Hermes Agent.

Hermes kann über Telegram Befehle erhalten wie:

```text
Starte den Server
Stoppe den Server
```

Damit ist **keine neue Telegram-Integration Teil dieses Projekts**, sofern Hermes bereits Prozesse/Dienste starten und stoppen kann.

Der Soul-Link-Dienst muss lediglich so deploybar sein, dass Hermes ihn zuverlässig starten und stoppen kann.

Bevorzugt:

```text
systemctl start <service>
systemctl stop <service>
systemctl restart <service>
```

oder entsprechend der bereits vorhandenen Deploymentstruktur.

---

# 5. Komponenten

## 5.1 SoulLink Server

Der Server ist die zentrale Autorität für den Zustand eines Runs.

Er verwaltet:

- Runs

- Spieler

- Client-Verbindungen

- Pokémon

- Fangorte

- Encounter

- Soul-Link-Gruppen

- aktive Teams

- Boxstatus

- Todesstatus

- Entwicklungen

- Synchronisationsaktionen

- Ereignishistorie

- optional Discord-Nachrichten

Der Server entscheidet insbesondere:

- welche Pokémon miteinander verlinkt sind

- welcher Encounter zu welcher Route gehört

- ob ein Encounter bereits verwendet wurde

- welche Soul-Link-Gruppe aktiv ist

- welche Pokémon bei anderen Spielern ins Team bzw. in die Box müssen

- wie Konflikte behandelt werden

Der Client soll möglichst wenig Soul-Link-Geschäftslogik besitzen.

---

## 5.2 SoulLink Client

Auf jedem Spieler-PC läuft ein kleiner Client.

Technologievorgabe:

- C#

- möglichst gleiche .NET-Version wie das Serverprojekt, sofern sinnvoll

- Windows als primäre Zielplattform

Der Client hat folgende Aufgaben:

- mGBA erkennen

- Verbindung zur lokalen Lua-Brücke herstellen

- aktuelle Spielinformationen empfangen

- Zustandsänderungen erkennen bzw. entgegennehmen

- Verbindung zum SoulLink Server herstellen

- Events an den Server senden

- Serverkommandos empfangen

- Kommandos an mGBA/Lua weiterreichen

- lokale Verbindungsausfälle puffern

- grundlegenden Verbindungsstatus anzeigen

Der Client soll selbst möglichst wenig Konfiguration benötigen.

---

## 5.3 mGBA Lua Bridge

Innerhalb von mGBA läuft ein Lua-Script.

Dieses Script ist die einzige Komponente, die direkt mit dem emulierten Spielzustand arbeitet.

Aufgaben:

### Lesen

Mindestens:

- aktuelle Partygröße

- Pokémon der Party

- Pokémon in PC-Boxen

- Species

- Level

- Personality/PID

- Trainer-/OT-Daten soweit erforderlich

- eindeutige Identifikation eines konkreten Pokémon

- HP

- Status

- Entwicklung

- aktuelle Map

- Map Group

- Map Number

- Region Map Section

- Spielzustand soweit erforderlich

- PC-/Boxaktivität soweit feststellbar

### Schreiben

Langfristig:

- Pokémon Party → Box verschieben

- Pokémon Box → Party verschieben

- ggf. weitere kontrollierte Zustandsänderungen

Schreibzugriffe dürfen niemals blind auf beliebige RAM-Adressen erfolgen.

Pokémon Gen 3 verwendet komplexe Pokémon-Datenstrukturen inklusive verschlüsselter Substrukturen und Checksumme.

Die Implementierung muss die Pokémon-Datenstrukturen korrekt behandeln.

---

# 6. Kommunikation mGBA ↔ Client

Lua und lokaler Client kommunizieren nur über localhost.

Bevorzugt:

```text
127.0.0.1
```

Kein Lua-Port darf extern erreichbar sein.

Mögliche Protokolle:

- TCP Socket

- alternativ WebSocket, falls mGBA-Lua-Unterstützung und Implementierungsaufwand sinnvoll sind

TCP mit einem einfachen framed JSON-Protokoll ist ausreichend.

Beispiel:

```json
{
  "type": "state.party_changed",
  "sequence": 1234,
  "timestamp": "2026-08-18T12:30:20Z",
  "payload": {
  }
}
```

Messages dürfen nicht davon abhängig sein, dass ein TCP-Paket exakt einer Nachricht entspricht.

Deshalb muss ein Framing verwendet werden, z. B.:

```text
JSON + newline
```

oder

```text
4-byte length prefix + JSON
```

Bevorzugt zunächst:

```text
JSON Lines
```

---

# 7. Kommunikation Client ↔ Server

Bevorzugtes Transportprotokoll:

```text
WebSocket Secure
wss://
```

Alternativ ASP.NET Core SignalR, sofern dies gut zum bestehenden Projekt passt.

Der Client baut die Verbindung aktiv zum Server auf.

Dadurch sind bei den Spielern erforderlich:

- keine geöffneten Ports

- kein Port Forwarding

- kein VPN

- kein Hamachi

- keine direkte Peer-to-Peer-Kommunikation

Kommunikationsprinzip:

```text
Client A ─┐
Client B ─┼── Server ─── Discord
Client C ─┘
```

Clients kommunizieren niemals direkt miteinander.

---

# 8. Authentifizierung

Ein Spieler tritt einem Run über einen Einladungscode bei.

Beispiel:

```text
RUBY-7K3F
```

Beim ersten Verbinden:

```text
Invitation Code
→ Run finden
→ Spieler auswählen/anlegen
→ Client registrieren
```

Danach soll ein langlebiges Client-Token lokal gespeichert werden.

Der Benutzer soll nicht bei jedem Start erneut einen Code eingeben müssen.

Das Token darf nicht im Klartext geloggt werden.

---

# 9. Run-Modell

Ein Run besitzt mindestens:

```text
Id
Name
GameVersion
RomIdentification
CreatedAt
Status
DiscordGuildId
DiscordChannelId
Players[]
Rules
```

Status beispielsweise:

```text
Created
Active
Paused
Finished
Archived
```

---

# 10. Spieler

Ein Spieler besitzt mindestens:

```text
PlayerId
RunId
Name
DiscordUserId optional
ClientId
ConnectionStatus
GameState
LastSeen
```

ConnectionStatus:

```text
Offline
Connecting
Online
OutOfSync
Error
```

---

# 11. Pokémon-Identität

Sehr wichtig:

Ein Pokémon darf nicht ausschließlich über Species identifiziert werden.

Beispiel:

```text
Spieler besitzt zwei Ralts.
```

Das System muss unterscheiden können, welches davon zu welchem Soul-Link gehört.

Daher soll intern eine stabile Pokémon-Identität erzeugt werden.

Mögliche Grundlage:

- Personality Value / PID

- OT Trainer ID

- Secret ID sofern erforderlich

- Species

- ggf. weitere Eigenschaften

Beispielsweise:

```text
PokemonIdentity =
Hash(
    OTId,
    PID,
    Species
)
```

Codex soll anhand der Gen-3-Datenstruktur prüfen, welche Kombination eine hinreichend stabile Identifikation ermöglicht.

Zusätzlich erhält jedes bekannte Pokémon serverseitig eine eigene:

```text
SoulLinkPokemonId : Guid
```

---

# 12. Pokémon-Modell

Mindestens:

```text
PokemonId
RunId
PlayerId

GameIdentity
Species
Nickname
Level
CurrentHp
MaxHp
Status

IsEgg
IsShiny

CurrentContainer
CurrentPartySlot
CurrentBox
CurrentBoxSlot

EncounterId
SoulLinkGroupId

IsDead

CreatedAt
LastUpdatedAt
```

Nicht alle Felder müssen in MVP1 benötigt werden.

Das Modell soll jedoch Erweiterungen erlauben.

---

# 13. Fang-Erkennung

Das System muss erkennen, wenn ein neues Pokémon erfolgreich gefangen wurde.

Nicht ausreichend:

```text
PartySize vorher 5
PartySize nachher 6
```

Denn bei voller Party wird das Pokémon direkt an den PC gesendet.

Robuster Ansatz:

Vorherigen bekannten Zustand vergleichen mit:

- Party

- relevanten PC-Boxen

- ggf. Save-/Game-State

Wenn eine bisher unbekannte Pokémon-Identität auftaucht, kann ein Fangereignis erzeugt werden.

Dabei muss ausgeschlossen werden:

- Pokémon wurde lediglich zwischen Box und Team verschoben

- Pokémon wurde entwickelt

- Pokémon wurde umbenannt

- Save State geladen

- Spielstand geladen

- bereits bekanntes Pokémon taucht erneut auf

Event:

```json
{
  "type": "pokemon.caught",
  "playerId": "...",
  "pokemon": {
    "identity": "...",
    "species": "Ralts",
    "level": 6
  },
  "location": {
    "mapGroup": 0,
    "mapNumber": 17,
    "regionMapSection": "ROUTE_102"
  }
}
```

---

# 14. Fangort

Bei jedem Fang muss der Ort gespeichert werden.

Mindestens:

```text
MapGroup
MapNumber
RegionMapSectionId
RegionMapSectionName
```

Beispiele:

```text
ROUTE_101
ROUTE_102
PETALBURG_CITY
PETALBURG_WOODS
GRANITE_CAVE
```

Die Soul-Link-Logik verwendet normalerweise:

```text
RegionMapSection
```

als Encounter-Gebiet.

MapGroup + MapNumber werden zusätzlich gespeichert, um später genauere Regeln zu ermöglichen.

---

# 15. Encounter-Modell

```text
Encounter
{
    Id
    RunId
    AreaId
    DisplayName
    Status
    PlayerEncounters[]
    SoulLinkGroupId
}
```

Mögliche Status:

```text
Waiting
PartiallyCaught
Complete
Failed
Dead
```

Beispiel:

```text
Route 102

Marco:
Ralts

Tobias:
Lotur

Daniel:
Seedot
```

Sobald alle erforderlichen Spieler ein gültiges Pokémon für dasselbe Encounter-Gebiet besitzen:

```text
SoulLinkGroup erstellen
```

---

# 16. Soul-Link-Gruppe

Eine Soul-Link-Gruppe verbindet jeweils genau ein Pokémon pro Spieler.

Beispiel:

```text
SoulLinkGroup #17

Marco   → Ralts
Tobias  → Lotur
Daniel  → Seedot
```

Modell:

```text
SoulLinkGroup
{
    Id
    RunId
    EncounterId

    Members[]

    IsActive
    IsDead

    CreatedAt
    DiedAt
}
```

---

# 17. Team-Synchronisation

Das System soll langfristig sicherstellen:

> Wenn ein Spieler ein Pokémon einer Soul-Link-Gruppe ins Team nimmt, werden die entsprechenden Pokémon derselben Gruppe bei allen anderen Spielern ebenfalls aktiviert.

Beispiel:

```text
Marco:
Ralts aus Box → Party
```

Server:

```text
Ralts gehört SoulLinkGroup #17
```

Daraus folgt:

```text
Tobias:
Lotur → Party

Daniel:
Seedot → Party
```

---

# 18. Box-Synchronisation

Umgekehrt:

```text
Marco:
Ralts Party → Box
```

führt zu:

```text
Tobias:
Lotur Party → Box

Daniel:
Seedot Party → Box
```

Dabei darf der Server nicht einfach lokale Entscheidungen überschreiben, wenn dadurch ein ungültiger Zustand entsteht.

---

# 19. Teamkonflikte

Beispiel:

Marco aktiviert SoulLinkGroup #17.

Tobias besitzt aktuell bereits sechs Pokémon im Team.

Der Server darf NICHT einfach zufällig eines entfernen.

Stattdessen:

```text
SyncOperation
Status = Blocked
Reason = PartyFull
```

Alle beteiligten Clients erhalten die Information.

Beispiel UI:

```text
Synchronisierung nicht möglich.

Tobias hat bereits 6 Pokémon im Team.

Bitte zuerst einen Platz freimachen.
```

Die ursprüngliche Änderung sollte möglichst entweder:

### Variante A – transaktional

erst endgültig synchronisiert werden, wenn alle Teilnehmer sie durchführen können.

oder

### Variante B – kurzfristig inkonsistent

lokal zulassen, aber den gesamten Run als `OutOfSync` markieren.

Bevorzugt wird langfristig Variante A.

Für MVP darf zunächst Variante B genutzt werden, wenn Rollback auf Spielebene zu riskant ist.

---

# 20. Serverseitige SyncOperation

Teamänderungen sollen als eigene Operation behandelt werden.

Beispiel:

```text
SyncOperation
{
    Id
    RunId
    InitiatingPlayerId
    SoulLinkGroupId

    Action
    Status

    Targets[]

    CreatedAt
    CompletedAt
}
```

Action:

```text
Activate
Deactivate
Kill
```

Status:

```text
Pending
Validated
Executing
Completed
Blocked
Failed
TimedOut
```

Targets:

```text
PlayerId
PokemonId
ExpectedAction
Status
Error
```

---

# 21. Sichere Spielmodifikation

Serverkommandos dürfen nicht zwangsläufig sofort ausgeführt werden.

Der Lua-Code muss feststellen, ob sich das Spiel in einem sicheren Zustand befindet.

Schreibzugriffe vermeiden während beispielsweise:

- Savevorgang

- Boxdatenmutation

- Pokémon-Menümutation

- Fangsequenz

- Übergang zwischen Maps

- Laden eines Spielstands

- ggf. Kampf

- sonstigen Zuständen, bei denen dieselben Strukturen gerade verändert werden

Kommando:

```json
{
  "type": "command.party.activate",
  "operationId": "...",
  "pokemonIdentity": "..."
}
```

Lua:

```text
Command empfangen
↓
Safe-State prüfen
↓
ggf. warten
↓
Operation durchführen
↓
Result senden
```

Response:

```json
{
  "type": "command.result",
  "operationId": "...",
  "success": true
}
```

---

# 22. Keine Pokémon-Neuerzeugung

Für normale Soul-Link-Synchronisierung sollen Pokémon nicht neu erzeugt werden.

Wenn ein Pokémon bereits beim jeweiligen Spieler existiert:

```text
vorhandenes Pokémon verschieben
```

statt:

```text
neues Pokémon erzeugen
```

Dadurch bleiben erhalten:

- PID

- IV

- EV

- Nature

- Moves

- PP

- OT

- Experience

- Friendship

- Status

- Individualität des Pokémon

---

# 23. Pokémon Gen-3-Datenstruktur

Die Implementierung muss die echte Pokémon-Ruby-Datenstruktur berücksichtigen.

Insbesondere:

- BoxPokemon

- Pokemon

- Personality Value

- OT ID

- verschlüsselte Substructures

- Substructure Permutation

- Encryption Key

- Checksum

Codex soll hierfür bevorzugt verifizierbare Quellen verwenden, insbesondere:

```text
pret/pokeruby
```

Keine Magic Numbers ohne Quellenkommentar.

RAM-Adressen und Struct-Offets sollen zentral definiert sein.

Beispielsweise:

```text
PokemonRubyMemoryMap
PokemonStructLayout
```

und nicht über Lua-Code verteilt.

---

# 24. ROM-Kompatibilität

MVP-Ziel:

```text
Pokémon Rubin
```

Die genaue ROM-Version muss identifiziert werden.

RAM-Adressen unterscheiden sich potenziell zwischen:

- Ruby US

- Ruby EU

- Ruby DE

- Ruby Versionen/Revisionen

- ROM Hacks

Der Client/Lua-Code darf deshalb nicht einfach unbekannte ROMs akzeptieren.

Beim Start:

```text
ROM erkennen
↓
bekannte Version?
```

Wenn nein:

```text
Unsupported ROM
```

Keine Schreibzugriffe.

Optional darf Read-Only-Modus angeboten werden.

---

# 25. Save States

Save States sind ein kritischer Sonderfall.

Beispiel:

```text
12:00 Ralts gefangen
12:05 gespeichert
12:10 Soul-Link verändert
12:15 Save State von 11:55 geladen
```

Dadurch kann der Spielzustand plötzlich rückwärts springen.

Das System muss mindestens erkennen können:

```text
lokaler Zustand widerspricht Serverzustand massiv
```

und nicht automatisch daraus neue Fang-/Tod-/Boxevents erzeugen.

MVP:

```text
OutOfSync markieren
Synchronisierung pausieren
Benutzer informieren
```

Später kann ein Reconciliation-System ergänzt werden.

---

# 26. Verbindungsausfälle

Der Spieler darf wegen eines kurzen Netzwerkausfalls nicht zwingend das Spiel stoppen müssen.

Client:

```text
Server offline
↓
lokale Events puffern
↓
Reconnect versuchen
↓
Events nachliefern
```

Jedes Event erhält:

```text
EventId
SequenceNumber
Timestamp
```

Serverevents müssen idempotent verarbeitet werden.

Dasselbe Event zweimal darf keine zwei Pokémon oder Soul-Link-Gruppen erzeugen.

---

# 27. Event Sequencing

Jeder Client führt eine monotone:

```text
SequenceNumber
```

Beispiel:

```text
1001 pokemon.caught
1002 pokemon.party_changed
1003 pokemon.evolved
```

Server speichert:

```text
LastProcessedSequence
```

Dadurch können:

- Duplikate ignoriert

- Lücken erkannt

- Reconnects behandelt

werden.

---

# 28. Heartbeat

Clients senden regelmäßig Heartbeats.

Beispiel:

```json
{
  "type": "heartbeat",
  "playerId": "...",
  "sequence": 12345,
  "gameState": "Overworld"
}
```

Server kann dadurch erkennen:

```text
Online
Offline
Stale
```

---

# 29. Reconnect

Nach Reconnect wird nicht blind weitergemacht.

Client sendet zunächst:

```text
State Snapshot
```

beispielsweise:

```json
{
  "type": "state.snapshot",
  "party": [],
  "boxes": [],
  "map": {}
}
```

Server vergleicht:

```text
Expected State
vs.
Actual State
```

Danach:

```text
Synchronized
```

oder:

```text
OutOfSync
```

---

# 30. Zustandsmodell Client

Clientstatus:

```text
Starting
WaitingForMgba
ConnectingToLua
ConnectingToServer
Synchronizing
Ready
Disconnected
OutOfSync
Error
```

---

# 31. Client UI

Die UI soll klein und funktional bleiben.

Keine komplexe Desktop-Anwendung.

Beispiel:

```text
Pokémon SoulLink
────────────────────────────

Spieler: Marco
Run: Ruby Soul-Link

● mGBA verbunden
● Server verbunden
● Spiel erkannt
● Synchronisiert

Aktuelle Route:
Route 102

Team:
Marshtomp
Ralts
Taillow

[Minimieren]
```

Optional:

```text
Tray Icon
```

Der Client soll nach erfolgreicher Einrichtung weitgehend unsichtbar laufen können.

---

# 32. Ersteinrichtung Client

Ziel:

```text
SoulLinkClient.exe starten
↓
Invitation Code eingeben
↓
Spieler auswählen
↓
fertig
```

Danach persistieren:

```text
Server URL
Client Token
Player ID
Run ID
```

---

# 33. mGBA-Integration

Der Client soll nach Möglichkeit erkennen:

```text
läuft mGBA?
```

Optional später:

```text
mGBA automatisch starten
ROM automatisch starten
Lua automatisch laden
```

Nicht voraussetzen, dass dies im ersten MVP zuverlässig automatisierbar ist.

Für MVP ist akzeptabel:

```text
mGBA starten
Tools → Scripting
SoulLink.lua laden
```

Danach verbindet sich Lua automatisch zum lokalen Client.

---

# 34. Automatischer Launcher – später

Zielversion:

```text
SoulLink.exe
```

Benutzer klickt:

```text
Pokémon starten
```

Client startet:

```text
mGBA
+
richtige ROM
+
SoulLink Integration
```

Danach braucht der Benutzer mGBA selbst praktisch nicht zu konfigurieren.

---

# 35. Discord-Integration

Discord ist nicht der Kommunikationskanal zwischen Clients.

Discord erhält Informationen vom zentralen Server.

Beispiel Fang:

```text
Marco hat auf Route 102 ein Ralts Lv. 6 gefangen.
```

Wenn die Gruppe vollständig ist:

```text
🔗 Neues Soul-Link-Paar

Route 102

Marco: Ralts
Tobias: Lotur
Daniel: Seedot
```

Teamänderung:

```text
🔄 Soul-Link-Team aktualisiert

Ralts ↔ Lotur ↔ Seedot wurde aktiviert.
```

Tod:

```text
☠ Soul-Link verloren

Route 102

Ralts
Lotur
Seedot
```

---

# 36. Discord Commands

Optional bzw. später:

```text
/soullink status
/soullink team
/soullink encounters
/soullink players
/soullink sync
/soullink pause
/soullink resume
```

Administrationscommands:

```text
/soullink run create
/soullink run finish
/soullink invite
```

Bestehende Command-Struktur des Bots bevorzugen.

---

# 37. Weboberfläche

Nicht Bestandteil des ersten MVP.

Architektur aber so gestalten, dass später eine Weboberfläche ergänzt werden kann.

Mögliche Ansicht:

```text
RUN: Ruby Soul-Link

Players
────────────────────────
Marco      Online
Tobias     Online
Daniel     Online

Teams
────────────────────────
Marco      Tobias      Daniel
Ralts      Lotur       Seedot
Taillow    Wingull     Zubat

Encounters
────────────────────────

Route 101
Zigzagoon  Wurmple     Poochyena
Alive

Route 102
Ralts      Lotur       Seedot
Dead
```

---

# 38. Tod eines Pokémon

Langfristig soll erkannt werden, wenn ein Pokémon stirbt.

Die konkrete Soul-Link-Regel muss konfigurierbar sein.

Beispielsweise:

```text
HP erreicht 0
```

Nicht jeder HP=0-Zustand muss automatisch permanent als Tod gelten, falls Run-Regeln etwas anderes definieren.

Daher:

```text
DeathRule
```

serverseitig konfigurierbar halten.

Beim bestätigten Tod:

```text
Pokemon.IsDead = true
SoulLinkGroup.IsDead = true
```

Alle Gruppenmitglieder werden entsprechend behandelt.

Ob die anderen Pokémon automatisch ebenfalls auf 0 HP gesetzt werden oder lediglich nicht mehr benutzt werden dürfen, ist eine separate Regel.

Keine irreversible Spielmanipulation ohne explizit implementierte Regel.

---

# 39. Entwicklung

Wenn sich Species ändert, die Pokémon-Identität aber gleich bleibt:

```text
pokemon.evolved
```

Beispiel:

```text
Ralts
→ Kirlia
```

SoulLinkPokemonId bleibt identisch.

SoulLinkGroup bleibt identisch.

Nur Species wird aktualisiert.

---

# 40. Nickname

Nicknameänderungen ändern nicht die Pokémon-Identität.

Optional Event:

```text
pokemon.renamed
```

---

# 41. Route und Map

Lua soll regelmäßig den aktuellen Ort bestimmen.

Event nur bei Änderung:

```text
map.changed
```

Nicht jedes Frame an Server schicken.

Beispiel:

```json
{
  "type": "map.changed",
  "mapGroup": 0,
  "mapNumber": 17,
  "regionMapSectionId": 12,
  "displayName": "Route 102"
}
```

---

# 42. Event-Driven statt Polling zum Server

Lua darf intern pro Frame prüfen.

Aber Client und Server sollen nur Zustandsänderungen kommunizieren.

Nicht:

```text
60x pro Sekunde vollständige Party senden
```

sondern:

```text
Party Hash vorher
Party Hash jetzt

gleich:
nichts tun

anders:
State analysieren
Event senden
```

---

# 43. State Hashing

Zur effizienten Änderungserkennung können Hashes verwendet werden:

```text
PartyStateHash
BoxStateHash
MapState
```

Bei Hashänderung werden relevante Strukturen detailliert gelesen.

---

# 44. Logging

Server verwendet strukturiertes Logging.

Beispiel:

```text
RunId
PlayerId
ClientId
EventId
OperationId
SoulLinkGroupId
```

Keine sensitiven Tokens loggen.

Log-Level:

```text
Trace
Debug
Information
Warning
Error
Critical
```

Lua-Debuglogging optional aktivierbar.

---

# 45. Persistenz

Falls das bestehende Projekt bereits eine Datenbank verwendet:

```text
bestehende DB-Technologie bevorzugen
```

Andernfalls für den Anfang beispielsweise:

```text
SQLite
```

für einfache Installation.

Auf dem zentralen Server wäre langfristig auch PostgreSQL sinnvoll.

Keine neue Datenbanktechnologie einführen, wenn das bestehende Projekt bereits eine geeignete besitzt.

---

# 46. Server-Neustart

Nach Serverrestart müssen Runs erhalten bleiben.

Clients reconnecten automatisch.

Server lädt:

```text
Run
Players
Pokémon
SoulLinks
ExpectedState
```

danach State-Reconciliation.

---

# 47. Client-Neustart

Client lädt:

```text
Token
Run
Player
Server
```

Verbindet sich mit:

```text
mGBA
Server
```

und führt Snapshot-Synchronisierung durch.

---

# 48. Server ausgeschaltet

Wenn Hermes bzw. Administrator den Server stoppt:

Client zeigt:

```text
Server nicht verfügbar.
```

Optional kann weitergespielt werden.

Lokale Events werden gepuffert.

Da allerdings komplexe Soul-Link-Synchronisierung ohne Serverautorität riskant ist, soll der Client deutlich anzeigen:

```text
Offline-Modus – automatische Synchronisierung pausiert.
```

---

# 49. Sicherheit

Mindestens:

- WSS/TLS für Internetkommunikation

- Client-Token

- Run-basierte Autorisierung

- keine offenen Lua-Ports ins Internet

- Server validiert alle Clientmessages

- Größenlimits für Messages

- Rate Limits soweit sinnvoll

- keine beliebigen Lua-Kommandos vom Server

- ausschließlich typisierte/whitelistete Commands

- kein Remote-Code-Execution-Mechanismus

Insbesondere darf es NICHT möglich sein:

```text
server → execute arbitrary Lua
```

sondern nur beispielsweise:

```text
PartyActivateCommand
PartyDeactivateCommand
RequestSnapshotCommand
```

---

# 50. Vertrauensmodell

Clients gelten nicht als vollständig vertrauenswürdig.

Server muss mindestens prüfen:

```text
Player gehört Run?
Pokemon gehört Player?
Pokemon gehört SoulLinkGroup?
Operation erlaubt?
Encounter existiert?
```

Für einen privaten Freundes-Soul-Link ist kein Anti-Cheat-System erforderlich.

---

# 51. Versionierung des Protokolls

Alle Verbindungen müssen eine Protokollversion besitzen.

Beispiel:

```json
{
  "protocolVersion": 1
}
```

Handshake:

```text
Client
→ Protocol 1

Server
→ supported
```

Bei inkompatibler Version:

```text
Client update required
```

---

# 52. Capability Negotiation

Da später verschiedene Spiele unterstützt werden könnten:

Client meldet Fähigkeiten:

```json
{
  "game": "PokemonRuby",
  "romVersion": "...",
  "capabilities": [
    "party.read",
    "box.read",
    "location.read",
    "party.write"
  ]
}
```

Damit kann der Server erkennen, ob ein Client beispielsweise nur Read-Only unterstützt.

---

# 53. Adapter-Architektur

Pokémon-Ruby-spezifische Logik darf nicht tief in der allgemeinen Serverlogik stecken.

Sinnvolle Trennung:

```text
SoulLink.Domain
SoulLink.Server
SoulLink.Client
SoulLink.Protocol
SoulLink.GameAdapters
```

Ruby-spezifisch:

```text
PokemonRubyAdapter
PokemonRubyMemoryMap
PokemonRubyPokemonParser
PokemonRubyLocationResolver
```

Keine Überabstraktion erzwingen, wenn das bestehende Repository eine einfachere Struktur nahelegt.

---

# 54. Perspektivische Unterstützung anderer Spiele

Nicht Teil des MVP.

Architektur soll aber nicht verhindern:

```text
Pokémon Sapphire
Pokémon Emerald
FireRed
LeafGreen
```

später zu ergänzen.

---

# 55. Projektstruktur – nur als Orientierung

Codex soll die bestehende Solution zuerst analysieren.

Falls passend könnte eine Struktur ungefähr so aussehen:

```text
ExistingDiscordBot.sln

src/
    DiscordBot/

    SoulLink.Domain/

    SoulLink.Protocol/

    SoulLink.Client/

    SoulLink.GameAdapters/
        PokemonRuby/

mgba/
    SoulLink.lua

tests/
    SoulLink.Domain.Tests/
    SoulLink.Protocol.Tests/
    SoulLink.Server.Tests/
    SoulLink.GameAdapters.Tests/
```

Nicht blind exakt diese Struktur erzeugen.

---

# 56. Tests

Serverlogik soll möglichst ohne Emulator testbar sein.

Besonders testen:

### Encounter

```text
drei Fänge gleiche Route
→ eine SoulLinkGroup
```

### Dubletten

```text
gleiches Event zweimal
→ nur ein Pokémon
```

### Entwicklung

```text
Ralts → Kirlia
→ gleiche PokemonId
```

### Boxwechsel

```text
Member A activated
→ entsprechende Commands B/C
```

### Party voll

```text
B hat sechs Pokémon
→ Operation Blocked
```

### Disconnect

```text
Client verliert Verbindung
→ Event Queue
→ Reconnect
→ Replay
```

### Reihenfolge

```text
Sequence 100
Sequence 102
→ Gap erkennen
```

### Serverrestart

```text
Persistierter Run
→ reload
→ reconciliation
```

---

# 57. Lua-Tests

Da Lua/mGBA schwieriger automatisiert testbar ist:

Parsinglogik soweit möglich von Emulatorzugriff trennen.

Beispielsweise:

```text
read memory
→ raw bytes
→ parser
```

Parser mit bekannten Pokémon-Strukturen testen.

Wenn sinnvoll können kleine Fixture-Dumps verwendet werden.

---

# 58. Observability / Diagnose

Der Client soll eine Diagnoseansicht besitzen.

Beispiel:

```text
mGBA:
Connected

ROM:
Pokemon Ruby DE
Supported

Lua:
Protocol 1

Server:
Connected
35 ms

Last Event:
party.changed

Last Sync:
OK
```

Optional:

```text
Export Diagnostics
```

Keine Secrets exportieren.

---

# 59. MVP-Phasen

Die Umsetzung soll NICHT mit automatischen Schreibzugriffen beginnen.

## MVP 0 – Repositoryanalyse

Noch kein produktiver Code.

Codex soll:

1. bestehende Solution analysieren

2. relevante Projekte identifizieren

3. Architektur beschreiben

4. Integrationspunkte identifizieren

5. Risiken nennen

6. konkreten Umsetzungsplan erstellen

7. notwendige Entscheidungen nennen

Danach erst Implementierung.

---

## MVP 1 – mGBA Read Proof of Concept

Ziel:

```text
Pokémon Ruby läuft
↓
Lua
↓
lokaler Client
```

Anzeigen:

```text
aktueller Ort
aktuelle Party
```

Mindestens:

```text
Species
Level
Pokemon Identity
Map
```

Noch kein Server erforderlich.

Akzeptanzkriterium:

> Pokémon oder Route im Spiel verändern → Client erkennt Änderung automatisch.

---

## MVP 2 – Server Connection

Client verbindet sich mit bestehendem C#-Server.

Server kennt:

```text
Run
Player
Connection
```

Heartbeat und Snapshot funktionieren.

---

## MVP 3 – Fang-Erkennung

Automatisch erkennen:

```text
Pokémon gefangen
+
Fangort
```

Server persistiert Encounter.

Discord meldet Fang.

---

## MVP 4 – Soul-Link-Zuordnung

Mehrere Spieler fangen auf derselben Route Pokémon.

Server erstellt automatisch:

```text
SoulLinkGroup
```

Discord meldet vollständige Gruppe.

---

## MVP 5 – Team Tracking

Server kennt jederzeit:

```text
welche Soul-Link-Gruppen aktiv sind
```

Noch keine Schreibzugriffe.

Discord/Client kann Unterschiede melden.

---

## MVP 6 – Write Proof of Concept

Erster kontrollierter Schreibtest.

Nicht sofort automatische Netzwerksynchronisierung.

Ziel:

```text
vorhandenes Pokémon
Box → Party
```

über:

```text
Server/Client Command
→ Lua
→ mGBA
```

Danach:

```text
Party → Box
```

Validieren:

- Pokémon bleibt korrekt

- Checksumme korrekt

- Savegame bleibt intakt

- keine Duplikation

- kein Pokémonverlust

---

## MVP 7 – Automatische Team-Synchronisation

Box-/Partyänderung bei Spieler A:

```text
→ Server
→ B/C
→ automatisch gleiche SoulLinkGroup aktivieren/deaktivieren
```

Mit Konfliktbehandlung.

---

## MVP 8 – Tod / Entwicklung

Automatisch erkennen:

```text
Evolution
Death
```

und Soul-Link-Zustand aktualisieren.

---

## MVP 9 – Komfort

Optional:

- Tray

- Auto Start mGBA

- Auto Load Lua

- Einladungsworkflow

- Update-System

- Web UI

---

# 60. Explizite Nicht-Ziele des ersten MVP

Nicht sofort implementieren:

- komplexe Weboberfläche

- mehrere Pokémon-Generationen

- ROM Hacks

- Cloud-Microservices

- Kubernetes

- Message Broker

- Redis ohne konkreten Bedarf

- Event Sourcing Framework

- CQRS Framework

- eigene Accountplattform

- automatische Updates

- Anti-Cheat

- P2P

- künstlich generierte Pokémon

- universelle Emulatorunterstützung

Keep it simple.

---

# 61. Designprinzipien

Priorität:

```text
1. Zuverlässigkeit
2. kein Pokémon-/Savegame-Verlust
3. einfache Bedienung
4. klare Architektur
5. Wiederverwendbarkeit
6. Performance
```

Nicht:

```text
maximale Abstraktion
```

---

# 62. UX-Ziel

Für einen normalen Spieler soll der gesamte Ablauf langfristig ungefähr so sein:

```text
SoulLinkClient.exe starten

↓ einmalig

Invite Code eingeben

↓ danach

Pokémon starten

↓
spielen
```

Der Spieler soll im normalen Betrieb NICHT:

- Discordcommands schreiben

- Pokémon manuell dokumentieren

- Routen dokumentieren

- Soul-Link-Paare verwalten

- andere Spieler nach Teamänderungen fragen

- Tabellen pflegen

müssen.

---

# 63. Gewünschter Normalablauf

Beispiel:

```text
Marco betritt Route 102.

Marco fängt Ralts.

Client:
Fang erkannt.

Server:
Encounter Route 102 / Marco = Ralts.

Tobias fängt Lotur.

Daniel fängt Seedot.

Server:
Encounter vollständig.

SoulLinkGroup #17:
Ralts ↔ Lotur ↔ Seedot

Discord:
neue Gruppe.
```

Später:

```text
Marco öffnet PC.

Marco:
Ralts → Party

Client erkennt Änderung.

Server erkennt:
SoulLinkGroup #17 aktivieren.

Tobias:
Lotur → Party

Daniel:
Seedot → Party

Alle bestätigen.

Server:
SoulLinkGroup #17 Active.
```

---

# 64. Fehlerfall

```text
Marco:
Ralts → Party

Tobias:
Party bereits voll
```

Server:

```text
SyncOperation #42
Blocked
```

Clients:

```text
Soul-Link konnte nicht synchronisiert werden.

Tobias muss zuerst einen Teamplatz freimachen.
```

Keine zufälligen Pokémon verschieben.

---

# 65. Wichtige technische Regel

Der **Server ist die Soul-Link-Wahrheit**.

mGBA ist die Wahrheit über den unmittelbaren lokalen Spielzustand.

Daraus ergibt sich:

```text
Local Game State
        ↓
Client Observation
        ↓
Server Domain State
        ↓
Synchronization Decision
        ↓
Client Commands
        ↓
Local Game State
```

---

# 66. Schutz vor Feedback-Loops

Sehr wichtig.

Beispiel:

```text
Marco nimmt Ralts ins Team
→ Server synchronisiert Tobias
→ Lua von Tobias verschiebt Lotur
→ Client von Tobias erkennt Partyänderung
→ meldet sie wieder
```

Diese zweite Änderung darf nicht erneut eine neue Synchronisierungskaskade starten.

Daher müssen SyncOperations korreliert werden.

Beispielsweise:

```text
OperationId
```

Wenn eine lokale Änderung Ergebnis einer RemoteOperation ist:

```text
Origin = Synchronization
OperationId = xyz
```

Server erkennt:

```text
Bestätigung bestehender Operation
```

statt:

```text
neue Benutzeraktion
```

---

# 67. Concurrency

Zwei Spieler könnten nahezu gleichzeitig Teamänderungen durchführen.

Server muss Operationen pro Run serialisieren oder anderweitig deterministisch auflösen.

Ein einfacher Ansatz:

```text
Async lock / queue per Run
```

Keine globale Sperre für alle Runs.

---

# 68. Datenverlust verhindern

Vor ersten Write-Funktionen:

- Backups von Savegames

- bekannte Test-ROM/Test-Saves

- umfangreiche Validierung

- nach Schreiboperation Pokémon erneut lesen

- Checksumme validieren

- Operation nur als erfolgreich markieren, wenn Readback korrekt ist

---

# 69. Feature Flags

Riskante Funktionen sollten getrennt aktivierbar sein.

Beispielsweise:

```text
SoulLink:EnableReadTracking=true
SoulLink:EnableDiscordEvents=true
SoulLink:EnableRemoteWrites=false
SoulLink:EnableAutoTeamSync=false
```

Default für neue Installation:

```text
RemoteWrites = false
AutoTeamSync = false
```

bis Write-PoC validiert wurde.

---

# 70. Konfiguration

Server beispielsweise:

```json
{
  "SoulLink": {
    "Enabled": true,
    "ListenUrl": "...",
    "EnableDiscord": true,
    "EnableRemoteWrites": false,
    "EnableAutoTeamSync": false
  }
}
```

Client:

```json
{
  "Server": "...",
  "LocalMgbaPort": 12345
}
```

Secrets nicht ins Repository committen.

---

# 71. Dokumentation

Erstellen:

```text
docs/soullink/architecture.md
docs/soullink/protocol.md
docs/soullink/mgba.md
docs/soullink/development.md
```

Dokumentieren:

- Architektur

- Setup

- Protokoll

- ROM-Unterstützung

- Memory Layout Quellen

- Debugging

- bekannte Einschränkungen

---

# 72. Codex-Arbeitsauftrag

Arbeite NICHT sofort blind die komplette Spezifikation ab.

## Schritt 1

Analysiere zuerst das vorhandene Repository vollständig genug, um zu verstehen:

- Solutionstruktur

- vorhandene Architektur

- Hosting

- DI

- Discord

- Persistenz

- Deployment

- Tests

- Logging

- Config

## Schritt 2

Vergleiche die bestehende Architektur mit dieser Spezifikation.

Erstelle anschließend einen konkreten Implementierungsplan.

Für jeden Schritt angeben:

```text
Ziel
betroffene Dateien/Projekte
neue Komponenten
Abhängigkeiten
Risiken
Tests
```

## Schritt 3

Identifiziere Punkte dieser Spezifikation, die technisch falsch, riskant oder unnötig kompliziert sind.

Nicht einfach blind übernehmen.

Insbesondere die mGBA- und Pokémon-Ruby-Memory-Integration anhand verlässlicher Quellen verifizieren.

## Schritt 4

Schlage die kleinstmögliche Architektur vor, die das Ziel erfüllt und gut zum bestehenden Repository passt.

Keine neuen Infrastrukturkomponenten ohne konkreten Nutzen.

## Schritt 5

Beginne anschließend mit:

```text
MVP 1 – mGBA Read Proof of Concept
```

Nicht mit automatischen RAM-Schreibzugriffen beginnen.

## Schritt 6

Nach jedem MVP:

- Tests ausführen

- Build prüfen

- Ergebnis dokumentieren

- offene Risiken nennen

- nächstes MVP identifizieren

---

# 73. Entscheidungsfreiheit für Codex

Diese Spezifikation beschreibt gewünschtes Verhalten, keine unumstößliche interne Architektur.

Codex darf technische Details ändern, wenn dadurch:

- Integration mit bestehendem Code besser wird

- weniger Komplexität entsteht

- Sicherheit steigt

- Testbarkeit steigt

- mGBA-Einschränkungen dies erforderlich machen

Solche Abweichungen sollen aber begründet werden.

---

# 74. Definition of Done für die erste große Ausbaustufe

Die erste produktiv nutzbare Version gilt als erreicht, wenn drei Spieler:

1. SoulLink Client starten.

2. Pokémon Rubin in mGBA spielen.

3. automatisch mit einem zentralen Run verbunden sind.

4. Pokémon fangen können.

5. Fang und Route automatisch erkannt werden.

6. serverseitig passende Encounter entstehen.

7. vollständige Soul-Link-Gruppen automatisch erzeugt werden.

8. Discord automatisch darüber informiert.

9. aktuelle Teams automatisch erkannt werden.

10. Reconnect nach kurzem Verbindungsverlust funktioniert.

Automatische Manipulation der Teams ist für diese Definition of Done noch **nicht zwingend erforderlich**.

Sie wird erst aktiviert, wenn die Write-Funktion separat ausreichend validiert wurde.

---

# 75. Langfristige Definition of Done

Die vollständige Vision ist erreicht, wenn zusätzlich:

1. Spieler ein Soul-Link-Pokémon normal über den Pokémon-PC aktivieren können.

2. der Wechsel automatisch erkannt wird.

3. der Server die zugehörige Soul-Link-Gruppe bestimmt.

4. die anderen Clients entsprechende Änderungen erhalten.

5. die jeweiligen Pokémon automatisch zwischen Box und Party verschoben werden.

6. alle Clients die Änderung bestätigen.

7. Konflikte sauber erkannt werden.

8. kein Pokémon dupliziert oder verloren gehen kann.

9. Entwicklungen automatisch verfolgt werden.

10. Todesfälle automatisch verarbeitet werden.

11. Discord und optional Web-UI jederzeit den korrekten Run-Zustand zeigen.

Das Endergebnis soll sich nicht wie ein externer Tracker anfühlen, sondern wie eine zusätzliche Soul-Link-Netzwerkschicht zwischen mehreren ansonsten normalen Pokémon-Rubin-Instanzen.
