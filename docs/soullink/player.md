# Für Mitspieler: SoulLink starten

Du bekommst **eine vorbereitete Spielerdatei als EXE**. Per Doppelklick starten. SoulLink öffnet mGBA, lädt seine Lesebrücke und verbindet dich mit dem Run.

Beim ersten Start einer neuen Spielerdatei wählst du die lokale `.gba`-Datei für diesen Run aus. Wenn du deine ROM auf die EXE ziehst, startet das Spiel direkt. Danach genügt ein Doppelklick. Andere Runs haben eigene Spielerdateien und eigene ROM-Auswahlen.

Im Spiel wie gewohnt fortsetzen oder neu anfangen. SoulLink zeigt dein Team, sobald du mindestens ein Pokémon hast. Falls mGBA das Skriptfenster mit öffnet, kannst du direkt ins Spielfenster wechseln. Das Spiel speichert wie gewohnt in mGBA. SoulLink liest nur mit.

Für einen ROM-Wechsel mGBA schließen und in SoulLink **Einrichtung → Spiel → Andere ROM auswählen** öffnen. Falls mGBA bereits offen ist, fordert SoulLink vor dem Start zum Schließen auf. So läuft jeweils nur eine Emulatorinstanz.

## Anmeldung am gemeinsamen Run

Hat der Gastgeber deine Datei vorbereitet, sind Server, Einladung und dein Name schon enthalten. Die Anmeldung und spätere Wiederverbindung erfolgen automatisch; ist der Server noch offline, versucht der Client es erneut.

Bei einer allgemeinen, noch nicht vorbereiteten Datei gibst du diese Angaben einmal unter **Einrichtung → Run verbinden** ein. Die Anmeldung wird für folgende Starts gespeichert.

## Für Gastgeber

Im Client unter **Einrichtung → Für Gastgeber** Serveradresse, Einladung aus `/run-init` und den Namen des Mitspielers eintragen. **Spielerdatei erstellen** speichert eine neue EXE-Kopie. Nur diese Datei weitergeben.

Die Kopie enthält die Einladungsdaten und einen eigenen Profilbezeichner. Sie enthält keine gespeicherten Zugangstokens, ROMs, Spielstände oder Testdaten. Die Einladung gilt für eine noch offene Lobby. Erstelle für jeden Mitspieler und jeden neuen Run eine eigene Datei.

Für den Betrieb mit einem laufenden manuellen Run gelten weiterhin die Hinweise der [Server-Anleitung](README.md): einen getrennten Test-Bot und eine eigene Testdatenbank benutzen. Der neue Client übernimmt keine bestehenden manuellen Links nachträglich.

Aktuell steht das [Profil für deutsches Rubin](mgba.md) zur Verfügung. Der Emulatorstart und die pro Run gespeicherte ROM-Auswahl bleiben für weitere Profile gleich.
