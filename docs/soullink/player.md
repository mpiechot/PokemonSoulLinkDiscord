# Für Mitspieler: SoulLink starten

Du bekommst **eine Datei: `SoulLink.exe`**. Es sind keine .NET-Installation, kein Terminal und keine Webseite nötig.

1. Lass mGBA mit deinem Spiel laufen.
2. Starte `SoulLink.exe`.
3. Sobald „Mit deinem Spiel verbunden“ erscheint, kannst du weiterspielen.

Der Client enthält keine Beispielspieler, Test-Pokémon oder Demo-Runs. Ohne echte Spieldaten zeigt er eine leere Warteansicht. Er startet und beendet mGBA nicht und verändert deinen Spielstand nicht.

## Einmalige Verbindung mit mGBA

mGBA muss die Lesebrücke laden. Dafür gibt es im Client **Einrichtung → mGBA verbinden**:

1. **Pfad kopieren** anklicken.
2. In mGBA **Tools → Scripting → File → Load script** öffnen.
3. Den kopierten Pfad in die Dateiauswahl einfügen und die Datei laden.

Die Brücke steckt bereits in der EXE und wird automatisch in einem festen Benutzerordner bereitgestellt. Du musst keine zweite Datei herunterladen oder bearbeiten.

Wenn deine mGBA-Version unter **Scripting → File** die Verwaltung der Autorun-Skripte anbietet, füge die gleiche Datei dort hinzu. Dann lädt mGBA sie bei künftigen Starts automatisch. Ältere Versionen können ein erneutes Laden verlangen. Bei bereits laufendem mGBA gibt es keine dokumentierte externe Schnittstelle, über die dieser Client das Skript ohne diesen Schritt laden könnte. Der Client versucht deshalb keine Eingabe- oder Speicherinjektion.

## Anmeldung am gemeinsamen Run

Hat der Gastgeber deine Datei vorbereitet, sind Server, Einladung und dein Name schon enthalten. Die Anmeldung und spätere Wiederverbindung erfolgen automatisch; ist der Server noch offline, versucht der Client es erneut.

Bei einer allgemeinen, noch nicht vorbereiteten Datei gibst du diese Angaben einmal unter **Einrichtung → Run verbinden** ein. Die Anmeldung wird für folgende Starts gespeichert.

## Für Gastgeber

Im Client unter **Einrichtung → Für Gastgeber** Serveradresse, Einladung aus `/run-init` und den Namen des Mitspielers eintragen. **Spielerdatei erstellen** speichert eine neue EXE-Kopie. Nur diese Datei weitergeben.

Die Kopie enthält die Einladungsdaten und einen eigenen Profilbezeichner. Sie enthält keine gespeicherten Zugangstokens, ROMs, Spielstände oder Testdaten. Die Einladung gilt für eine noch offene Lobby. Erstelle für jeden Mitspieler eine eigene Datei und prüfe beim ersten Test, ob die angezeigte Party zu dessen Spiel passt.

Für den Betrieb mit einem laufenden manuellen Run gelten weiterhin die Hinweise der [Server-Anleitung](README.md): einen getrennten Test-Bot und eine eigene Testdatenbank benutzen. Der neue Client übernimmt keine bestehenden manuellen Links nachträglich.

Aktuell gilt weiterhin das [AXVD-Referenzprofil](mgba.md): deutsche Rubin-ROM, strukturell geprüft, noch nicht mit deiner konkreten Randomizer-ROM bestätigt.
