# Für Mitspieler: SoulLink starten

Du bekommst **eine Spielerdatei als EXE**. Beim ersten Start wählst du deinen Spielernamen, dein Karten-Icon und deine lokale `.gba`-Datei. Danach reicht ein Doppelklick. SoulLink öffnet mGBA mit Lesebrücke und verbindet dich mit dem Run.

Andere Runs haben eigene Spielerdateien und eigene ROM-Auswahlen. Wenn du deine ROM auf die EXE ziehst, übernimmt SoulLink diese Auswahl direkt.

Im Spiel wie gewohnt fortsetzen oder neu anfangen. mGBA erscheint links mit seiner eigenen Menüleiste. Mit **Spiel fokussieren** gehen Tastatureingaben zurück an den Emulator. Über **mGBA lösen** kannst du zum separaten Emulatorfenster wechseln. Rechts siehst du Team, Links, Box, Todesfälle, Shinies und Run-Verlauf. Die Minimap zeigt die originale Hoenn-Karte aus deiner ROM und platziert Spieler in ihrem aktuellen Gebiet. Gestrichelte Icons zeigen die letzte bekannte Position. Die nächste Arena richtet sich nach deinen Orden. SoulLink verwendet die Level aus der aktiven Rubin-ROM, wenn deren Trainertabelle erkannt wird, und kennzeichnet sonst die Standardlevel.

Falls mGBA beim Start sein Skriptfenster öffnet, kannst du weiterhin im Spielfenster spielen. SoulLink liest das Spiel über die mitgelieferte Brücke. Gespeichert wird wie gewohnt im Emulator.

Für einen ROM-Wechsel mGBA schließen und in SoulLink **Einrichtung → Spiel → Andere ROM auswählen** öffnen. Falls mGBA bereits offen ist, fordert SoulLink vor dem Start zum Schließen auf. So läuft jeweils nur eine Emulatorinstanz.

## Anmeldung am gemeinsamen Run

Hat der Gastgeber die Datei vorbereitet, sind Server und Einladung schon enthalten. Du wählst Namen und Icon einmal selbst. Ist der Server noch offline, versucht SoulLink es mit deiner Auswahl erneut.

Bei einer allgemeinen, noch nicht vorbereiteten Datei gibst du diese Angaben einmal unter **Einrichtung → Run verbinden** ein. Die Anmeldung wird für folgende Starts gespeichert.

## Für Gastgeber

Im Client unter **Einrichtung → Für Gastgeber** Serveradresse und Einladung aus `/run-init` eintragen. **Spielerdatei erstellen** speichert eine neue EXE-Kopie für den gesamten Run. Alle Mitspieler erhalten dieselbe Datei.

Die Kopie enthält die Einladungsdaten und einen Profilbezeichner pro Run. Die Einladung gilt für eine noch offene Lobby. Erstelle für jeden neuen Run eine eigene Datei.

Für den Betrieb mit einem laufenden manuellen Run gelten weiterhin die Hinweise der [Server-Anleitung](README.md): einen getrennten Test-Bot und eine eigene Testdatenbank benutzen. Der neue Client übernimmt keine bestehenden manuellen Links nachträglich.

Aktuell steht das [Profil für deutsches Rubin](mgba.md) zur Verfügung. Für den lokalen Bildtest liegen die optionalen Pokémon-Sprites neben der EXE unter `sprites/`. Ohne diesen Ordner zeigt die Leiste Typfarben und Pokédex-Nummern.
