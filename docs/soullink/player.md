# Für Mitspieler: SoulLink starten

Du bekommst **eine Spielerdatei als EXE**. Beim ersten Start wählst du deinen Spielernamen, dein Karten-Icon und deine lokale `.gba`-Datei. Danach reicht ein Doppelklick. SoulLink öffnet mGBA mit Lesebrücke und verbindet dich mit dem Run.

Andere Runs haben eigene Spielerdateien und eigene ROM-Auswahlen. Wenn du deine ROM auf die EXE ziehst, übernimmt SoulLink diese Auswahl direkt.

Im Spiel wie gewohnt fortsetzen oder neu anfangen. mGBA erscheint in der Mitte mit seiner eigenen Menüleiste. Ein Klick ins Spiel gibt ihm den Tastaturfokus. Über **mGBA lösen** kannst du zum separaten Emulatorfenster wechseln.

Links zeigt der Newsfeed neue Fänge und weitere Ereignisse. Rechts stehen Team, Links, Box, Todesfälle, Shinies und die Übersicht **Fangorte**. Jedes Pokémon zeigt seinen im Spiel gespeicherten Fangort. Der **Catch-Check** akzeptiert deutsche und englische Namen und prüft die Evolutionsfamilie gegen die erfassten Pokémon des Runs.

Die Minimap verwendet die originale Hoenn-Karte aus deiner ROM. Im Freien wird deine Position aus der aktuellen Spielfeldkachel auf die Weltkarte übertragen. In Gebäuden erscheint der Marker am zugehörigen Gebiet. Gestrichelte Icons zeigen die letzte bekannte Position.

Die nächste Arena richtet sich nach deinen Orden. **Abschließen** verbucht sie für deinen Spieler. **Zurücknehmen** nimmt den zuletzt verbuchten Arena-Fortschritt zurück. Für erkannte Arenateams zeigt SoulLink die Level aus der geladenen Rubin-ROM. Für andere ROM-Varianten gelten die Rubin-Standardlevel.

Nach den ersten gültigen Spieldaten schließt SoulLink das bei der automatischen Skripteinrichtung geöffnete Fenster. Gespeichert wird wie gewohnt im Emulator. Der lokale Spieltest startet seinen Feed beim ersten gültigen Spielstand. Im gemeinsamen Run beginnt die Fang-Erfassung mit dem Start der Lobby.

Neue Runs fragen bei einem erkannten Kampfunfähigkeitsfall nach. Im Dialog kannst du den Tod bestätigen und einen Grund angeben. Erkannte Gegner und die zuletzt erfasste Attacke werden angezeigt. Die Bestätigung markiert den gesamten Link als tot. **Später** hält den Fall unter **Todesfall prüfen** offen.

Für einen ROM-Wechsel mGBA schließen und in SoulLink **Einrichtung → Spiel → Andere ROM auswählen** öffnen. Falls mGBA bereits offen ist, fordert SoulLink vor dem Start zum Schließen auf. So läuft jeweils nur eine Emulatorinstanz.

## Anmeldung am gemeinsamen Run

Hat der Gastgeber die Datei vorbereitet, sind Server und Einladung schon enthalten. Du wählst Namen und Icon einmal selbst. Ist der Server noch offline, versucht SoulLink es mit deiner Auswahl erneut.

Bei einer allgemeinen, noch nicht vorbereiteten Datei gibst du diese Angaben einmal unter **Einrichtung → Run verbinden** ein. Die Anmeldung wird für folgende Starts gespeichert.

## Für Gastgeber

Im Client unter **Einrichtung → Für Gastgeber** Serveradresse und Einladung aus `/run-init` eintragen. **Spielerdatei erstellen** speichert eine neue EXE-Kopie für den gesamten Run. Alle Mitspieler erhalten dieselbe Datei.

Die Kopie enthält die Einladungsdaten und einen Profilbezeichner pro Run. Die Einladung gilt für eine noch offene Lobby. Erstelle für jeden neuen Run eine eigene Datei.

Für den Betrieb mit einem laufenden manuellen Run gelten weiterhin die Hinweise der [Server-Anleitung](README.md): einen getrennten Test-Bot und eine eigene Testdatenbank benutzen. Der neue Client übernimmt keine bestehenden manuellen Links nachträglich.

Aktuell steht das [Profil für deutsches Rubin](mgba.md) zur Verfügung. Für den lokalen Bildtest liegen die optionalen Pokémon-Sprites neben der EXE unter `sprites/`. Ohne diesen Ordner zeigt die Leiste Typfarben und Pokédex-Nummern.
