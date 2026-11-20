# Für Mitspieler: SoulLink starten

Du bekommst **einmal die SoulLink.exe**. Öffne **SoulLink → Run beitreten / wechseln**, füge die Einladung ein und klicke **Beitreten**. Danach bestätigst du deinen Spielernamen und dein Karten-Icon und wählst deine lokale `.gba`-Datei. Ein Doppelklick auf die EXE öffnet später den zuletzt gewählten Run.

SoulLink merkt sich pro Run die Anmeldung und die ROM-Auswahl. Wenn du deine ROM auf die EXE ziehst, übernimmt SoulLink diese Auswahl direkt.

Im Spiel wie gewohnt fortsetzen oder neu anfangen. mGBA erscheint in der Mitte mit seiner eigenen Menüleiste. Ein Klick ins Spiel gibt ihm den Tastaturfokus. Das Menü **SoulLink** oben links enthält **Spiel starten**, **mGBA lösen** und **Einrichtung**. Unten zeigen die farbigen Punkte den Status von Run-Server und Spieldaten an. Beim Darüberfahren erscheint ein kurzer Hinweis.

Speichere wie gewohnt im Spiel. Das Kreuz im SoulLink-Fenster beendet auch das von SoulLink gestartete mGBA, einschließlich eines ausgelösten Fensters. Danach beendet SoulLink die lokale Lesebrücke und meldet den Spieler am gemeinsamen Run ab.

Links stehen Fänge, vollständige Links und Todesfälle im scrollbaren Newsfeed. Unter dem Spiel ordnet das Team-Grid die Spieler in Spalten und die verbundenen Pokémon in gemeinsamen Zeilen an. Oben öffnen **Box**, **Tot** und **Shinies** eigene Grids. **Fangorte** zeigt die bisher erfassten Fanggebiete als Tags. Der **Catch-Check** akzeptiert deutsche und englische Namen und prüft die Evolutionsfamilie gegen die erfassten Pokémon des Runs.

Die Minimap verwendet die originale Hoenn-Karte aus deiner ROM. Im Freien wird deine Position aus der aktuellen Spielfeldkachel auf die Weltkarte übertragen. In Gebäuden erscheint der Marker am zugehörigen Gebiet. Gestrichelte Icons zeigen die letzte bekannte Position.

Team und Karte zeigen im gemeinsamen Run die zuletzt vom Server angenommenen Daten. **Abgleich nötig** markiert einen erkannten Spielstandwechsel oder eine unerwartete Inventaränderung. Ist der aktuelle Spielstand korrekt, kann der Gastgeber ihn unter **Spieler und Spielstände → Spielstand neu abgleichen** freigeben. Der Client verbindet sich daraufhin selbst neu.

Die nächste Arena richtet sich nach dem gemeinsamen Fortschritt. **Abschließen** und **Zurücknehmen** gelten für alle Spieler im Run. Für erkannte Arenateams zeigt SoulLink die Level aus der jeweils geladenen Rubin-ROM. Für andere ROM-Varianten gelten die Rubin-Standardlevel.

**Cheats** zeigt Sonderbonbons, Pokébälle, Superbälle, Hyperbälle und Meisterbälle. Kehre im Spiel aufs Spielfeld zurück. Ein Klick auf **+99** legt 99 Stück der ausgewählten Sorte ins Item-Lager deines PCs. Der Dialog meldet den neuen Bestand. Hole die Items am PC ab und speichere wie gewohnt im Spiel.

**Regeln** zeigt die SoulLink-Grundregeln und eure Zusatzregeln. Alle angemeldeten Teilnehmer können dem gemeinsamen Run eine Zusatzregel hinzufügen. Im lokalen Spieltest speichert SoulLink Zusatzregeln mit dem lokalen Verlauf.

Sobald die Lesebrücke läuft, schließt sich das Scripting-Fenster automatisch. Die Spieldaten werden grün, sobald ihr im Spielfeld seid. Ein leeres Team ist dafür in Ordnung. Gespeichert wird wie gewohnt im Emulator. Der lokale Spieltest startet seinen Feed beim ersten gültigen Spielstand. Die allgemeine Spielerdatei zeigt den lokalen Feed auch bei einer gespeicherten Anmeldung, solange der Run-Server nicht verbunden ist. Die lokale Erfassung gilt nur für diese Sitzung. Sobald der Server wieder erreichbar ist, wechselt die Anzeige zum gemeinsamen Run. Dort erscheinen Fänge, sobald der Gastgeber den Run gestartet hat.

Neue Runs zeigen einen erkannten K. o. bereits im Kampf an. Das Popup öffnet sich ohne Tastaturfokus. Darin kannst du den Tod bestätigen und einen Grund angeben. SoulLink zeigt den Gegner und die K.-o.-Attacke, wenn die Lesebrücke den direkten Treffer zuordnen konnte. In anderen Fällen zeigt es die zuletzt erfasste Attacke, sofern bekannt. Die Bestätigung markiert den gesamten Link als tot. **Später** hält den Fall unter **Todesfall prüfen** offen.

Für einen ROM-Wechsel mGBA schließen und in SoulLink **Einrichtung → Spiel → Andere ROM auswählen** öffnen. Falls mGBA bereits offen ist, fordert SoulLink vor dem Start zum Schließen auf. So läuft jeweils nur eine Emulatorinstanz.

## Anmeldung am gemeinsamen Run

Für einen neuen Run bekommst du eine Einladung als Text. Derselbe Dialog öffnet auch gespeicherte Runs. Die Einladung enthält die Serveradresse und den Run-Namen. SoulLink schlägt deinen bisherigen Namen und dein Icon vor, wenn du schon in einem Run angemeldet bist. Die Anmeldung und ROM-Auswahl werden für jeden Run einzeln gespeichert.

## Für Gastgeber

Für einen Server ohne Discord öffnest du **SoulLink → Gastgeber**. Gib die HTTPS-Adresse und den Admin-Schlüssel ein und klicke auf **Verbinden / aktualisieren**. Lege eine Gruppe und darin einen Run an. Über **Einladung kopieren** erhältst du den Text zum Weitergeben. Der Run wird dabei auch für dich gemerkt. Sobald alle Mitspieler gültige Spieldaten übertragen haben, aktualisierst du die Ansicht und klickst auf **Starten**. Pause, Fortsetzen, Abschließen und der neue Abgleich eines Spielstands stehen ebenfalls in der Gastgeber-Ansicht.

Beginnt ihr gemeinsam ein neues Spiel, kannst du den Run starten, sobald alle im Spielfeld angekommen sind. SoulLink erfasst die frisch gewählten Starter automatisch. Mit **Links bearbeiten** kannst du Starter und Fänge im aktiven oder pausierten Run nachträglich zuordnen. Ziehe jedes Pokémon in seiner Spielerspalte zur gewünschten Zeile oder zu **Unverlinkt**. **Links speichern** übernimmt die Zuordnung für alle. Der Starter-Link lässt Route 101 für einen regulären Fang frei. Im Grid steht **Starter** auf den zugeordneten Pokémon. Beim Darüberfahren siehst du den ursprünglichen Fangort.

Unter **SoulLink → Run beitreten / wechseln** findest du die gemerkten Einladungen. Beim Wechsel bitte vorher im Spiel speichern. SoulLink beendet mGBA und öffnet dieselbe EXE mit dem gewählten Run-Profil.

### Einladungen per Discord

Füge den Einladungscode aus `/run-init` unter **SoulLink → Run beitreten / wechseln** ein und gib die Serveradresse im eingeblendeten Feld an. Die Einladung gilt für die noch offene Lobby.

Für den Betrieb mit einem laufenden manuellen Run gelten weiterhin die Hinweise der [Server-Anleitung](README.md): einen getrennten Test-Bot und eine eigene Testdatenbank benutzen. Der neue Client übernimmt keine bestehenden manuellen Links nachträglich.

### Zu zweit ohne Discord testen

1. Verbinde dich unter **SoulLink → Gastgeber** mit eurem HTTPS-Run-Server und lege eine Testgruppe mit einem Test-Run an.
2. Gib beiden Spielern einmal die SoulLink.exe. Schick ihnen die Einladung aus **Einladung kopieren**.
3. Beide fügen die Einladung unter **Run beitreten / wechseln** ein und wählen Name, Karten-Icon und lokale Rubin-ROM. Sobald beide im Spielfeld sind, klickt der Gastgeber auf **Verbinden / aktualisieren** und dann auf **Starten**.
4. Fangt beide ein Pokémon im gleichen Fanggebiet. Der Link erscheint auf beiden Rechnern. Fügt unter **Regeln** eine Testregel hinzu und prüft, ob sie beim anderen Spieler erscheint.

Für einen automatischen Test der Mehrspieler-Verbindung auf einem Rechner gibt es `./scripts/Start-SoulLinkDemo.ps1 -Verify`.

Aktuell steht das [Profil für deutsches Rubin](mgba.md) zur Verfügung. SoulLink verwendet die flachen Sammelkarten-Symbole und ergänzt für Typen wie Boden, Gestein und Flug die eigenen Spielsymbole aus Pokémon Legenden: Arceus. Jeder Typ hat ein eigenes Symbol und einen deutschen Tooltip. Heruntergeladene Bilder sind danach auch offline verfügbar. Die Icons des Discord-Bots sind in der EXE enthalten.

Der lokale Spieltest speichert seinen Feed und bestätigte Tode in einer privaten SQLite-Datei pro ROM und Trainer. Bereits erfasste Fänge behalten beim Schnellladen und Neustarten ihre ursprüngliche Uhrzeit. Aus dem Spielstand lässt sich der aktuelle Stand von Team und Box lesen. Fangzeiten und bestätigte SoulLink-Tode stammen aus der Run-Historie.
