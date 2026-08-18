# Entwicklungsworkflow

Dieser Workflow gilt fuer alle Aenderungen am Pokemon Soul Link Discord Bot. Er ergaenzt die verbindlichen Regeln in `AGENTS.md` und beschreibt den Ticket-Lifecycle fuer Entwickler und Reviewer.

## Ticket-Lifecycle

1. **Backlog**
   - Die Arbeit beginnt mit einem GitHub Issue im Project `Pokemon Soul Link Backlog`.
   - Das Issue enthaelt Ziel, Akzeptanzkriterien und ein passendes Label wie `feature`, `bugfix` oder `suggestion`.
2. **Analyse**
   - Vorhandene Implementierung, Tests, Abhaengigkeiten und Risiken werden geprueft.
   - Unklare fachliche Regeln werden im Issue geklaert, bevor eine riskante Annahme umgesetzt wird.
   - Bestehende Branches und Pull Requests zum Ticket werden wiederverwendet.
3. **Umsetzung**
   - Ein fokussierter Branch wird vom aktuellen `origin/main` erstellt.
   - Neue Logik wird nach Moeglichkeit testgetrieben umgesetzt.
   - Nicht zum Ticket gehoerende Verbesserungen werden nicht nebenbei eingebaut, sondern als separates `suggestion`-Issue erfasst.
4. **Tests**
   - Betroffene Tests werden waehrend der Entwicklung gezielt ausgefuehrt.
   - Vor dem Push werden Restore, Build und die vollstaendige Testsuite ausgefuehrt.
   - StyleCop-Warnungen in neuem oder geaendertem Code werden behoben.
5. **Review**
   - Der Diff wird auf fachliche Korrektheit, Lesbarkeit, unbeabsichtigte Aenderungen, Secrets und Line Endings geprueft.
   - Der Pull Request beschreibt Zweck, wichtigste Aenderungen und ausgefuehrte Verifikation.
   - Die erste Zeile der PR-Beschreibung lautet `Ticket: <Issue-Link>`.
   - Nach Erstellung des Pull Requests wird das Project Item auf `In Review` gesetzt.
6. **Done**
   - Review-Kommentare und CI-Checks sind erledigt.
   - Der Pull Request ist gemerged.
   - Das Issue ist geschlossen und das Project Item steht auf `Done`.

## Definition of Ready

Ein Ticket ist bereit zur Umsetzung, wenn:

- Ziel und erwarteter Nutzen verstaendlich beschrieben sind;
- pruefbare Akzeptanzkriterien vorhanden sind;
- fachliche Begriffe und betroffene Discord-Commands eindeutig sind;
- externe Abhaengigkeiten, Datenquellen und bekannte Risiken benannt sind;
- das Ticket ein passendes Label besitzt und im aktiven Project liegt;
- keine offene Entscheidung die grundlegende technische Richtung blockiert.

Kleine technische Details duerfen waehrend der Umsetzung entschieden werden. Annahmen, die Nutzerverhalten, gespeicherte Daten oder die Architektur wesentlich veraendern, muessen vorher geklaert werden.

## Definition of Done

Ein Ticket ist fertig, wenn:

- alle Akzeptanzkriterien umgesetzt und nachvollziehbar geprueft sind;
- neue oder geaenderte Logik durch passende Tests abgedeckt ist;
- Restore, Build und alle Tests erfolgreich ausgefuehrt wurden;
- neuer oder geaenderter Code keine StyleCop-Warnungen erzeugt;
- Dokumentation und Konfigurationshinweise zum Verhalten passen;
- der Diff keine unbeabsichtigten Formatierungs-, Whitespace-, Secret- oder Line-Ending-Aenderungen enthaelt;
- ein fokussierter Commit auf dem Ticket-Branch gepusht wurde;
- ein nicht als Draft markierter Pull Request das Issue verlinkt;
- CI und Review abgeschlossen sind;
- nach dem Merge Issue und Project Item auf `Done` stehen.

Nicht ausfuehrbare Checks werden im Pull Request mit Grund und verbleibendem Risiko dokumentiert.

## Lokale Verifikation

Alle Befehle werden im Repository-Root ausgefuehrt.

```powershell
dotnet restore PokeSoulLinkBot.Tests/PokeSoulLinkBot.Tests.csproj --verbosity minimal

dotnet build PokeSoulLinkBot/PokeSoulLinkBot.csproj `
  --no-restore `
  --no-incremental `
  --verbosity minimal `
  -p:UseAppHost=false `
  -p:UseSharedCompilation=false `
  -p:OutputPath=../artifacts/build/PokeSoulLinkBot/

dotnet test PokeSoulLinkBot.Tests/PokeSoulLinkBot.Tests.csproj `
  --no-restore `
  --verbosity minimal `
  -p:UseAppHost=false `
  -p:UseSharedCompilation=false `
  -p:OutputPath=../artifacts/test/PokeSoulLinkBot.Tests/
```

Fuer eine schnelle Rueckmeldung kann ein einzelner Test oder eine Testklasse gefiltert werden:

```powershell
dotnet test PokeSoulLinkBot.Tests/PokeSoulLinkBot.Tests.csproj `
  --no-restore `
  --filter "FullyQualifiedName~RunStoreTests"
```

Die CI verwendet dieselben Restore-, Build- und Testschritte aus `.github/workflows/ci.yml`.

## StyleCop und Codequalitaet

- StyleCop-Warnungen werden nicht unterdrueckt, nur um einen Check gruen zu bekommen.
- Eine Unterdrueckung ist nur sinnvoll, wenn die Regel fuer das gesamte Projekt bewusst nicht gilt und die Entscheidung nachvollziehbar konfiguriert ist.
- Namen, Verantwortlichkeiten und Kontrollfluesse sollen klein und eindeutig bleiben.
- Bei geaenderten Schnittstellen werden alle Implementierungen, Test-Doubles und Aufrufer angepasst.
- Der Verbesserungs-Check fragt ausdruecklich: Kann die Loesung robuster, einfacher, schneller, besser testbar oder fachlich hilfreicher werden?

## Line Endings und Diff-Pruefung

Das Repository verwendet gemaess `.editorconfig` und `.gitattributes` CRLF fuer Textdateien. Geaenderte Dateien werden vor dem Commit geprueft:

```powershell
git diff --check
git diff --stat
git diff
git ls-files --eol
```

Fuer geaenderte Textdateien wird in der Ausgabe von `git ls-files --eol` ein einheitliches Worktree-Format `w/crlf` erwartet. Gemischte Line Endings muessen vor dem Commit normalisiert werden.

## Pull Requests und Reviews

- Ein Pull Request behandelt genau ein Ticket oder dokumentiert explizit, warum eine Trennung nicht sinnvoll ist.
- Titel und Beschreibung sind auf Englisch und erklaeren das beobachtbare Verhalten.
- Relevante Labels werden vom Issue auf den Pull Request uebernommen.
- Zustimmung zu einem umgesetzten Review-Kommentar wird mit einer `+1`-Reaction markiert.
- Bei Widerspruch wird auf Englisch sachlich erklaert, warum der Vorschlag nicht oder anders umgesetzt wird.
- Force-Pushes sind nur nach ausdruecklicher Zustimmung erlaubt.
