# Backlog

Dieses Backlog sammelt fachliche und technische Aufgaben fuer die naechsten Iterationen.

## Offen

### BL-001 Arena-Fortschritt im Run speichern

- Status: offen
- Branch: noch keiner
- Ziel: Einen Command erstellen, mit dem festgehalten wird, dass eine Arena im laufenden Run erledigt ist.
- Akzeptanzkriterien:
  - Der Command verwendet den aktiven Run der Discord-Guild.
  - Die Arena kann eindeutig ausgewaehlt werden, idealerweise per Nummer und optionaler Edition.
  - Der erledigte Arena-Status wird persistiert.
  - Status-, Stats- oder Run-Ausgaben koennen den Arena-Fortschritt anzeigen.

### BL-005 StatsCommand ausbauen

- Status: offen
- Branch: noch keiner
- Ziel: Den StatsCommand so implementieren, dass er basierend auf den gesammelten Daten weitere Statistiken ausgibt.
- Moegliche Statistiken:
  - Anzahl gefangener, lebender und toter Routen.
  - Team- und Box-Groesse.
  - Todesgruende und haeufigste Ursachen.
  - Statistiken pro Spieler.
  - Arena-Fortschritt.
- Akzeptanzkriterien:
  - Ausgabe ist uebersichtlich und Discord-kompatibel.
  - Leere oder neue Runs werden sauber behandelt.
  - Tests decken typische und leere Runs ab.

### BL-006 Workflow fuer Ticket-Bearbeitung definieren

- Status: offen
- Branch: noch keiner
- Ziel: Einen klaren Workflow fuer das Bearbeiten von Tickets definieren.
- Akzeptanzkriterien:
  - Ticket-Lifecycle ist beschrieben: Backlog, Analyse, Umsetzung, Tests, Review, Done.
  - Definition of Ready und Definition of Done sind festgelegt.
  - Erwartete Test- und Build-Kommandos sind dokumentiert.
  - Umgang mit StyleCop-Warnungen und Line-Endings ist beschrieben.

### BL-007 Build der Konsolen-App erstellen

- Status: offen
- Branch: noch keiner
- Ziel: Einen Build der Konsolen-App bereitstellen, damit Visual Studio nicht vom Nutzer blockiert wird.
- Akzeptanzkriterien:
  - Es gibt einen dokumentierten Build-/Publish-Befehl fuer die Konsolen-App.
  - Der Output liegt in einem separaten Verzeichnis ausserhalb von `bin\\Debug`, damit VS/MSBuild nicht kollidieren.
  - Startanleitung fuer den gebauten Bot ist dokumentiert.
  - Der Build enthaelt alle benoetigten Ressourcen und Konfigurationsdateien.

### BL-010 Command fuer Fangbarkeits-Check

- Status: offen
- Branch: noch keiner
- Ziel: Einen Command erstellen, mit dem geprueft werden kann, ob ein Pokemon im aktiven Run noch gefangen werden darf.
- Vorschlag: `/catch-check`
- Akzeptanzkriterien:
  - Der Command akzeptiert einen Pokemon-Namen auf Deutsch oder Englisch.
  - Der Name wird auf eine eindeutige Pokemon-Spezies aufgeloest.
  - Der Check betrachtet alle bereits gefangenen Pokemon des aktiven Runs, unabhaengig davon, ob sie leben oder tot sind.
  - Fuer jedes bereits gefangene Pokemon wird auch dessen Evolutionslinie beruecksichtigt.
  - Wenn das angefragte Pokemon selbst oder ein Pokemon derselben Evolutionslinie bereits im Run vorkommt, meldet der Command, dass es nicht gefangen werden darf.
  - Wenn kein Treffer gefunden wird, meldet der Command, dass das Pokemon gefangen werden darf.
  - Die Ausgabe nennt bei einem Treffer das passende bereits gefangene Pokemon inklusive Route, Spieler und Status, soweit vorhanden.
  - Unbekannte oder mehrdeutige Pokemon-Namen werden mit einer klaren Fehlermeldung behandelt.
  - Tests decken deutsche und englische Namen, lebende und tote Pokemon, Evolutionslinien und den erlaubten Fall ohne Treffer ab.
 
### BL-011 Bugfix für WebSocket Anfragen

- Status: offen
- Branch: noch keiner
- Ziel: Folgender Fehler ist behoben: [17:24:25 ERR] Slash command /status failed after 5876 ms with parameters: none.
System.TimeoutException: Cannot respond to an interaction after 3 seconds!
   at Discord.WebSocket.SocketCommandBase.RespondAsync(String text, Embed[] embeds, Boolean isTTS, Boolean ephemeral, AllowedMentions allowedMentions, MessageComponent components, Embed embed, RequestOptions options, PollProperties poll, MessageFlags flags)
   at PokeSoulLinkBot.Bot.Commands.StatusCommand.HandleAsync(SocketSlashCommand command) in C:\MARPIE\Projekte\CSharp\PokemonSoulLinkDiscord\PokeSoulLinkBot\Bot\Commands\StatusCommand.cs:line 66
   at PokeSoulLinkBot.Bot.Handlers.SlashCommandRouter.HandleAsync(SocketSlashCommand command) in C:\MARPIE\Projekte\CSharp\PokemonSoulLinkDiscord\PokeSoulLinkBot\Bot\SlashCommandRouter.cs:line 70
[17:24:37 WRN] Discord Gateway: A SlashCommandExecuted handler has thrown an unhandled exception.
System.TimeoutException: Cannot respond to an interaction after 3 seconds!
   at Discord.WebSocket.SocketCommandBase.RespondAsync(String text, Embed[] embeds, Boolean isTTS, Boolean ephemeral, AllowedMentions allowedMentions, MessageComponent components, Embed embed, RequestOptions options, PollProperties poll, MessageFlags flags)
   at PokeSoulLinkBot.Bot.Handlers.SlashCommandRouter.HandleAsync(SocketSlashCommand command) in C:\MARPIE\Projekte\CSharp\PokemonSoulLinkDiscord\PokeSoulLinkBot\Bot\SlashCommandRouter.cs:line 95
   at Discord.EventExtensions.InvokeAsync[T](AsyncEvent`1 eventHandler, T arg)
   at Discord.WebSocket.DiscordSocketClient.TimeoutWrap(String name, Func`1 action)

### BL-012 Mehrere Runs parallel in einer Guild verwalten

- Status: offen
- Branch: noch keiner
- Ziel: Mehrere Runs sollen parallel gespeichert und ueber einen Gruppennamen geladen werden koennen, damit verschiedene Spielergruppen gleichzeitig spielen koennen und Testen leichter wird.
- Hintergrund: Der Bot arbeitet aktuell mit genau einem aktiven Run pro Guild. Fuer Spielbetrieb und lokale Tests ist es unpraktisch, wenn dafuer bestehende Runs beendet oder ueberschrieben werden muessen. Der Gruppenname soll kuenftig der fachliche Schluessel sein, ueber den ein Run gestartet, geladen und im Bot als aktiv gemerkt wird.
- Akzeptanzkriterien:
  - `/run-start` wird um einen verpflichtenden Gruppennamen erweitert.
  - Der Gruppenname wird mit dem Run persistiert und zur Identifizierung verwendet.
  - `/run-start` darf keinen neuen Run erzeugen, wenn fuer denselben Gruppennamen bereits ein nicht beendeter Run existiert.
  - Wenn `/run-start` mit einem Gruppennamen aufgerufen wird, unter dem aktuell kein laufender Run existiert, startet der Command normal einen neuen Run.
  - Es gibt einen neuen Command `/run-load <gruppenname>`, der alle gespeicherten Runs durchsucht und den ersten nicht beendeten Run fuer diesen Gruppennamen als aktiv setzt.
  - Wenn fuer den Gruppennamen kein laufender Run existiert, liefert `/run-load` eine klare Rueckmeldung.
  - Solange fuer einen Gruppennamen ein Run aktiv ist, sind fuer diesen Gruppennamen nur Laden oder Beenden des bestehenden Runs moeglich; ein weiterer Start ist blockiert.
  - Der Bot speichert pro Guild, welcher Gruppenname beziehungsweise welcher Run aktuell aktiv ist.
  - Nach einem Bot-Neustart wird der zuletzt aktive Run automatisch wieder aktiv gesetzt.
  - Wenn noch kein aktiver Run gespeichert ist, wird als Fallback der erste noch nicht beendete gespeicherte Run als aktiv gesetzt.
  - Alle bestehenden Commands arbeiten weiterhin ohne zusaetzlichen Gruppenname-Parameter und verwenden immer den aktuell aktiven Run.
  - Relevante Ausgaben der bestehenden Commands nennen den Gruppennamen des aktuell aktiven Runs zusaetzlich mit.
  - Es gibt einen separaten Command, der den Gruppennamen des aktuell aktiven Runs ausgibt.
  - Persistenz und Laden verhindern Datenverlust oder Verwechslungen zwischen parallelen Runs.
  - Tests decken Starten, Blockieren doppelter Gruppennamen, Laden, Neustart-Fallback, aktiven Kontext und parallele Runs ab.

### BL-013 Slash-Command-Antworten zentral absichern

- Status: offen
- Branch: noch keiner
- Ziel: Eine zentrale Strategie fuer Discord-Interaction-Antworten definieren, damit lang laufende Commands rechtzeitig deferen und konsistent antworten.
- Akzeptanzkriterien:
  - Commands nutzen ein einheitliches Muster fuer `DeferAsync`, Followups und Fehlerantworten.
  - Langsame Datenquellen koennen keinen 3-Sekunden-Interaction-Timeout mehr verursachen.
  - Tests oder gezielte Abdeckung pruefen, dass Router und Commands Antwortpfade korrekt behandeln.
  - Die Loesung passt zu bestehenden Embed- und Fehlerausgaben.

### BL-014 Run-Start-, Catch- und Route-Death-Tests nachziehen

- Status: offen
- Branch: noch keiner
- Ziel: Die Testabdeckung fuer `/run-start`, `/catch` und `/route-death` erweitern, damit fachliche Edgecases, Autocomplete-Verhalten und Command-Definitionen sicher abgedeckt sind.
- Hintergrund: Catch- und Route-Death-Logik ist auf Service-Ebene bereits gut getestet, Run-Start und Autocomplete-/Handler-Pfade haben aber noch Luecken.
- Akzeptanzkriterien:
  - `RunService.StartRun` ist fuer erfolgreiche Anlage, leere Spielerlisten, doppelte aktive Runs, getrennte Guilds und Whitespace-/Argumentvalidierung getestet.
  - `/run-start`, `/catch` und `/route-death` pruefen Command-Definitionen inklusive Pflicht-/Optional-Flags und Autocomplete-Flags.
  - Autocomplete-Tests decken leeren Katalog, nicht bereiten Katalog, bestehende Routen aus dem aktiven Run, Duplikate und partiellen Input ab.
  - Catch-Tests decken Route-Normalisierung, mehrere Spieler pro Route, Team-Zuordnung, volle Teams, Spieler ausserhalb des Runs und Persistenz ab.
  - Route-Death-Tests decken verlorene Routen ohne Pokemon, Default-Reason, getrimmte Reason/Player-Werte, bereits verlorene Routen, Routen mit Catches, Entfernen aus dem aktiven Team und Persistenz ab.
  - Falls direkte Handler-Tests sinnvoll und stabil umsetzbar sind, pruefen sie erfolgreiche Antwortpfade und relevante Fehlerpfade ohne Discord-API-Aufrufe.
