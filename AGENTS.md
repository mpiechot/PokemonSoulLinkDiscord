# Entwicklungsrichtlinien

Diese Richtlinien gelten fuer alle Entwicklungsaufgaben in diesem Repository.

## Projektkontext

Dieses Repository enthaelt einen .NET-9-Discord-Bot fuer Pokemon-Soul-Link-Runs. Der Bot unterstuetzt Discord-Guilds dabei, Runs mit Spielern, Editionen, Routen, gefangenen Pokemon, Team-/Box-Zustaenden, Toden, Arena-Fortschritt und Statistiken nachzuhalten.

Die Loesung besteht im Kern aus:

- `PokeSoulLinkBot`: Konsolen-/Host-Anwendung mit Discord.Net-Integration, Slash Commands, Presentation/Embed-Erzeugung, Application Services, Domain-Modellen und JSON-basierter Persistenz.
- `PokeSoulLinkBot.Tests`: xUnit-Testprojekt fuer Commands, Run-Logik, Embed-Ausgaben, Startup-Tasks und Datenservices.
- Externen Datenquellen wie PokeAPI und PokemonDB-nahen Daten fuer Pokemon-Namen, Pokedex-Informationen, Editionen, Routen/Location Areas und Arena-Informationen.

Beim Umsetzen einzelner Aufgaben soll das grosse Ganze im Blick bleiben: Der Bot soll fuer laufende Soul-Link-Runden schnell, robust, Discord-kompatibel und fuer Nutzer klar verstaendlich bleiben. Neue Funktionen sollen die Run-Daten konsistent halten, bestehende Commands nicht verlangsamen und Ausgaben so gestalten, dass sie direkt im Spielbetrieb helfen.

## Arbeitsrichtlinien

- Das aktive Backlog wird im GitHub Project `Pokemon Soul Link Backlog` gepflegt: https://github.com/users/mpiechot/projects/2
- Aufgaben werden nicht mehr aus lokalen Markdown-Dateien gelesen. `BACKLOG.md`, `DONE.md` und `SUGGESTIONS.md` werden nicht verwendet.
- Vor Beginn eines Tickets wird das passende GitHub Issue im Project geprueft. Falls der Nutzer eine fachliche ID wie `BL-013` oder `SG-004` nennt, wird das entsprechende Issue verwendet.
- Vor Beginn einer neuen Aufgabe werden alle Tickets in der Project-Spalte `In Review` geprueft. Wenn deren verlinkter Pull Request bereits gemerged ist, wird das Ticket geschlossen und das Project Item auf `Done` verschoben.
- Falls es noch kein passendes Issue gibt, wird vor der Umsetzung ein GitHub Issue mit aussagekraeftigem Titel, Beschreibung, Akzeptanzkriterien und passenden Labels angelegt und ins Project aufgenommen.
- Tickets erhalten passende Labels, insbesondere `feature`, `bugfix` oder `suggestion`.
- Vor Beginn der Umsetzung wird geprueft, ob es bereits einen passenden Branch gibt. Falls nicht, wird von `main` aus ein neuer Feature-Branch erstellt und vorher der aktuelle Stand von `origin/main` geholt.
- Aenderungen fuer ein Ticket bleiben auf dem zugehoerigen Branch. Nach Abschluss werden die relevanten Dateien geprueft, committed und auf den Branch gepusht.
- Sobald ein Pull Request erstellt wurde, muss das Ticket mit diesem Pull Request verlinkt sein.
- Die Pull-Request-Beschreibung beginnt immer in der ersten Zeile mit `Ticket: <Issue-Link>`, damit der Pull Request direkt auf das Ticket zurueckverweist.
- Sobald eine Aufgabe bearbeitet und der Pull Request erstellt wurde, wird das Project Item des Tickets auf `In Review` gesetzt.
- Wenn ein PR ein Ticket vollstaendig erledigt und gemerged wurde, wird das Issue geschlossen und das Project Item auf `Done` gesetzt.
- Git-Befehle fuer den normalen Ticket-Workflow duerfen ohne Rueckfrage ausgefuehrt werden, insbesondere `git add`, `git commit`, `git switch` und `git push` ohne Force-Optionen. Force-Pushes, auch `--force-with-lease`, duerfen nur nach ausdruecklicher Zustimmung ausgefuehrt werden.
- Falls fuer den Task noch kein Pull Request existiert, wird nach dem Push ein Pull Request erstellt. Wenn die Aufgabe aus Sicht von Codex abgeschlossen ist, darf der Pull Request nicht im Draft-Modus bleiben.
- Pull Requests werden mit aussagekraeftigem Titel, nachvollziehbarer Beschreibung, passenden Labels und, soweit bekannt, sinnvollen Reviewern oder weiteren Metadaten versehen.
- StyleCop-Anmerkungen werden immer behoben. Neuer oder geaenderter Code soll keine StyleCop-Warnungen einfuehren.
- Code wird mit Clean-Code-Prinzipien im Blick geschrieben: klare Namen, kleine fokussierte Einheiten, geringe Kopplung und gut lesbare Kontrollfluesse.
- SOLID-Prinzipien werden beachtet. Abhaengigkeiten, Verantwortlichkeiten und Erweiterungspunkte sollen bewusst geschnitten sein.
- Code soll getestet sein. Neue Logik bekommt passende Tests; bestehende Tests werden angepasst, wenn sich Verhalten bewusst aendert.
- Bei jeder Umsetzung gehoert ein ausdruecklicher Verbesserungs-Check zum Workflow: Es wird aktiv geprueft, ob die aktuelle Implementierung robuster, einfacher, schneller, besser testbar oder fachlich nuetzlicher werden kann.
- Dabei wird auch bewusst nach Folgeaufgaben, Risiken und neuen sinnvollen Features gesucht, die nicht Teil des aktuellen Tickets sind.
- Solche nicht zum Ticket gehoerenden Ideen werden nicht nebenbei umgesetzt, sondern als GitHub Issue mit Label `suggestion` angelegt und ins Project aufgenommen, damit der aktuelle Scope fokussiert bleibt.
- Vor einem Commit wird der Diff geprueft, insbesondere auf unbeabsichtigte Formatierungs-, Whitespace- oder Line-Ending-Aenderungen. Line Endings muessen dem im Repository gesetzten Standard entsprechen.
- Manuelle Datei-Aenderungen werden direkt mit CRLF geschrieben oder unmittelbar nach dem Patch auf CRLF normalisiert. Vor dem Commit wird mit `git ls-files --eol` geprueft, dass geaenderte Textdateien nicht mit gemischten Line Endings im Worktree liegen.
- Vor Abschluss einer Aufgabe werden relevante Formatierungs-, Analyse- und Testbefehle ausgefuehrt, soweit sie im Projekt verfuegbar und praktikabel sind.
- Alle relevanten Tests muessen vor Abschluss gruen sein. Falls ein Test nicht ausgefuehrt werden kann, wird der Grund dokumentiert.
- Bestehende Nutzer- oder Fremdaenderungen werden nicht ueberschrieben oder zurueckgesetzt, ausser dies wurde ausdruecklich beauftragt.
- Commits sollen fokussiert und nachvollziehbar sein. Commit- und PR-Beschreibungen nennen kurz Zweck, wichtigste Aenderungen und ausgefuehrte Verifikation.
