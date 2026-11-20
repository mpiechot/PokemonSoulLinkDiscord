# Für Mitspieler: SoulLink starten

Du bekommst **eine Spielerdatei als EXE**. Beim ersten Start wählst du deinen Spielernamen, dein Karten-Icon und deine lokale `.gba`-Datei. Danach reicht ein Doppelklick. SoulLink öffnet mGBA mit Lesebrücke und verbindet dich mit dem Run.

Andere Runs haben eigene Spielerdateien und eigene ROM-Auswahlen. Wenn du deine ROM auf die EXE ziehst, übernimmt SoulLink diese Auswahl direkt.

Im Spiel wie gewohnt fortsetzen oder neu anfangen. mGBA erscheint in der Mitte mit seiner eigenen Menüleiste. Ein Klick ins Spiel gibt ihm den Tastaturfokus. Das Menü **SoulLink** oben links enthält **Spiel starten**, **mGBA lösen** und **Einrichtung**. Unten zeigen die farbigen Punkte den Status von Run-Server und Spieldaten an. Beim Darüberfahren erscheint ein kurzer Hinweis.

Speichere wie gewohnt im Spiel. Das Kreuz im SoulLink-Fenster beendet auch das von SoulLink gestartete mGBA, einschließlich eines ausgelösten Fensters. Danach beendet SoulLink die lokale Lesebrücke und meldet den Spieler am gemeinsamen Run ab.

Links stehen Fänge, vollständige Links und Todesfälle im scrollbaren Newsfeed. Unter dem Spiel ordnet das Team-Grid die Spieler in Spalten und die verbundenen Pokémon in gemeinsamen Zeilen an. Oben öffnen **Box**, **Tot** und **Shinies** eigene Grids. **Fangorte** zeigt die bisher erfassten Fanggebiete als Tags. Der **Catch-Check** akzeptiert deutsche und englische Namen und prüft die Evolutionsfamilie gegen die erfassten Pokémon des Runs.

Die Minimap verwendet die originale Hoenn-Karte aus deiner ROM. Im Freien wird deine Position aus der aktuellen Spielfeldkachel auf die Weltkarte übertragen. In Gebäuden erscheint der Marker am zugehörigen Gebiet. Gestrichelte Icons zeigen die letzte bekannte Position.

Die nächste Arena richtet sich nach deinen Orden. **Abschließen** verbucht sie für deinen Spieler. **Zurücknehmen** nimmt den zuletzt verbuchten Arena-Fortschritt zurück. Für erkannte Arenateams zeigt SoulLink die Level aus der geladenen Rubin-ROM. Für andere ROM-Varianten gelten die Rubin-Standardlevel.

Nach den ersten gültigen Spieldaten schließt SoulLink das bei der automatischen Skripteinrichtung geöffnete Fenster. Gespeichert wird wie gewohnt im Emulator. Der lokale Spieltest startet seinen Feed beim ersten gültigen Spielstand. Die allgemeine Spielerdatei zeigt den lokalen Feed auch bei einer gespeicherten Anmeldung, solange der Run-Server nicht verbunden ist. Die lokale Erfassung gilt nur für diese Sitzung. Sobald der Server wieder erreichbar ist, wechselt die Anzeige zum gemeinsamen Run. Dort erscheinen Fänge, sobald der Gastgeber den Run gestartet hat.

Neue Runs zeigen einen erkannten K. o. bereits im Kampf an. Das Popup öffnet sich ohne Tastaturfokus. Darin kannst du den Tod bestätigen und einen Grund angeben. SoulLink zeigt den Gegner und die K.-o.-Attacke, wenn die Lesebrücke den direkten Treffer zuordnen konnte. In anderen Fällen zeigt es die zuletzt erfasste Attacke, sofern bekannt. Die Bestätigung markiert den gesamten Link als tot. **Später** hält den Fall unter **Todesfall prüfen** offen.

Für einen ROM-Wechsel mGBA schließen und in SoulLink **Einrichtung → Spiel → Andere ROM auswählen** öffnen. Falls mGBA bereits offen ist, fordert SoulLink vor dem Start zum Schließen auf. So läuft jeweils nur eine Emulatorinstanz.

## Anmeldung am gemeinsamen Run

Hat der Gastgeber die Datei vorbereitet, sind Server und Einladung schon enthalten. Du wählst Namen und Icon einmal selbst. Ist der Server noch offline, versucht SoulLink es mit deiner Auswahl erneut.

Bei einer allgemeinen, noch nicht vorbereiteten Datei gibst du diese Angaben einmal unter **Einrichtung → Run verbinden** ein. Die Anmeldung wird für folgende Starts gespeichert.

## Für Gastgeber

Im Client unter **Einrichtung → Für Gastgeber** Serveradresse und Einladung aus `/run-init` eintragen. **Spielerdatei erstellen** speichert eine neue EXE-Kopie für den gesamten Run. Alle Mitspieler erhalten dieselbe Datei.

Die Kopie enthält die Einladungsdaten und einen Profilbezeichner pro Run. Die Einladung gilt für eine noch offene Lobby. Erstelle für jeden neuen Run eine eigene Datei.

Für den Betrieb mit einem laufenden manuellen Run gelten weiterhin die Hinweise der [Server-Anleitung](README.md): einen getrennten Test-Bot und eine eigene Testdatenbank benutzen. Der neue Client übernimmt keine bestehenden manuellen Links nachträglich.

Aktuell steht das [Profil für deutsches Rubin](mgba.md) zur Verfügung. Die optionalen Pokémon-Sprites und originalen Typabzeichen liegen neben der EXE unter `sprites/`. Bei einer einzeln weitergegebenen EXE zeigt das Grid Typfarben und Pokédex-Nummern. Die Icons des Discord-Bots sind in der EXE enthalten.
