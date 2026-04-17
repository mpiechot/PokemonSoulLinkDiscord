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

### BL-002 Einheitliche Command-Ausgaben

- Status: offen
- Branch: noch keiner
- Ziel: Den Output aller Commands normalisieren, damit Antworten ein einheitliches Bild haben und Daten uebersichtlich angezeigt werden.
- Akzeptanzkriterien:
  - Einheitliche Embed- oder Textstruktur fuer Erfolg, Status, Fehler und Zusammenfassungen.
  - Einheitliche Benennung von Run, Edition, Route, Spieler, Pokemon und Status.
  - Lange Listen bleiben lesbar und Discord-kompatibel.
  - Bestehende Tests werden angepasst oder ergaenzt.

### BL-003 DeathCommand um Reason und verursachenden Spieler erweitern

- Status: offen
- Branch: noch keiner
- Ziel: Der DeathCommand bekommt eine Reason und optional einen Spieler, der Schuld war.
- Akzeptanzkriterien:
  - `reason` ist als Command-Parameter verfuegbar.
  - `player` ist optional als Command-Parameter verfuegbar.
  - Reason und Spieler werden persistiert.
  - Ausgabe zeigt Route, betroffene Pokemon, Reason und optionalen Spieler.

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

### BL-008 Game-Data-Katalog-Fetch beschleunigen und stabilisieren

- Status: offen
- Branch: noch keiner
- Hintergrund: Der erste Fetch der PokeAPI Location Areas ist langsam, weil fuer viele Location Areas einzelne Detail-Requests ausgefuehrt werden.
- Ziel: Startup und Autocomplete duerfen nicht davon abhaengen, dass alle Location Areas frisch geladen werden.
- Akzeptanzkriterien:
  - Der Cache liegt an einem stabilen Ort und geht nicht bei Clean/Rebuild verloren.
  - Es gibt eine klare Logmeldung, ob Cache, API oder Fallback verwendet wird.
  - Location-Area-Details werden begrenzt parallelisiert oder anderweitig schneller geladen.
  - Bei API-Fehlern bleiben Edition-Autocomplete und bestehende Route-Daten nutzbar.
  - Optional: Ein vorbefuellter JSON-Katalog wird ins Repository aufgenommen.

### BL-009 StyleCop-Dateikopf-Regel deaktivieren

- Status: erledigt
- Branch: `codex/disable-stylecop-file-header`
- Hintergrund: StyleCop meldet aktuell `The file header is missing or not located at the top of the file.`
- Ziel: Die File-Header-Warnung wird in der StyleCop-Konfiguration deaktiviert, weil dieses Repository keine verpflichtenden Dateikoepfe verwendet.
- Akzeptanzkriterien:
  - [x] Die betroffene StyleCop-Regel fuer fehlende Dateikoepfe ist zentral deaktiviert.
  - [x] Neue und bestehende Dateien muessen keinen File Header enthalten.
  - [x] StyleCop-Analyse oder Build laeuft ohne diese Warnung durch.
  - [x] Andere StyleCop-Regeln bleiben unveraendert aktiv.

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
