# Für Mitspieler: SoulLink starten

Ein Doppelklick auf **SoulLink.exe** zeigt die Startauswahl. **Run fortsetzen** lädt für den zuletzt gestarteten Run den neusten Spielstand aus Slot 1, Slot 2 oder einem Poké-Tausch-Sicherungspunkt. Bei gleichem Zeitstempel hat Slot 1 Vorrang. **Run laden** zeigt deine gespeicherten Run-Profile. Mit **Run beitreten** fügst du eine Einladung ein und wählst beim ersten Start deine lokale `.gba`-Datei, deinen Spielernamen und dein Karten-Icon.

SoulLink merkt sich pro Run die Anmeldung und die ROM-Auswahl. Wenn du deine ROM auf die EXE ziehst, übernimmt SoulLink diese Auswahl direkt.

Im Spiel wie gewohnt fortsetzen oder neu anfangen. mGBA erscheint in der Mitte mit seiner eigenen Menüleiste. Ein Klick ins Spiel gibt ihm den Tastaturfokus. Das Menü **SoulLink** oben links enthält **Spiel starten**, **mGBA lösen** und **Einrichtung**. Unten zeigen die farbigen Punkte den Status von Run-Server und Spieldaten an. Beim Darüberfahren erscheint ein kurzer Hinweis.

Im **SoulLink**-Menü legst du mit **Speichern (Slot 1)** einen manuellen mGBA-Spielstand ab. **Schnellladen (Slot 1)** stellt ihn wieder her und ist auch am Titelbildschirm verfügbar. Nach einem Kampf speichert SoulLink automatisch in **Slot 2**, sobald du wieder im Spielfeld bist. Das Kreuz im SoulLink-Fenster beendet auch das von SoulLink gestartete mGBA, einschließlich eines ausgelösten Fensters. Danach beendet SoulLink die lokale Lesebrücke und meldet den Spieler am gemeinsamen Run ab.

Links stehen Fänge, vollständige Links und Todesfälle im scrollbaren Newsfeed. Unter dem Spiel ordnet das Team-Grid die Spieler in Spalten und die verbundenen Pokémon in gemeinsamen Zeilen an. Oben öffnen **Box**, **Tot** und **Shinies** eigene Grids. **Fangorte** zeigt die bisher erfassten Fanggebiete als Tags. Der **Catch-Check** akzeptiert deutsche und englische Namen und prüft die Evolutionsfamilie gegen die erfassten Pokémon des Runs.

Die Minimap verwendet die originale Hoenn-Karte aus deiner ROM. Im Freien wird deine Position aus der aktuellen Spielfeldkachel auf die Weltkarte übertragen. In Gebäuden erscheint der Marker am zugehörigen Gebiet. Gestrichelte Icons zeigen die letzte bekannte Position.

Team und Karte zeigen im gemeinsamen Run die zuletzt vom Server angenommenen Daten. Nach Verbindungsabbrüchen, Inventaränderungen und Schnellladen gleicht SoulLink den Bestand desselben Trainers automatisch ab. Bisherige Fänge, Links und Tode bleiben in der Run-Historie. Erscheint **Anderer Spielstand**, lade den zum Run gehörenden Spielstand. Für einen absichtlichen Wechsel des Trainers kann der Gastgeber unter **Spieler und Spielstände → Spielstand neu abgleichen** den neuen Spielstand freigeben.

Sobald du in Rubin die Pokébälle von Professor Birchs Labor erhalten hast, zählt der erste bestätigte Wildkampf pro Fangort. Flucht oder ein besiegtes wildes Pokémon ohne Fang sperrt den Ort für die ganze Gruppe. Bereits zugehörige Pokémon zählen als tot. Im Newsfeed steht, wer den Erstfang verpasst hat und welche Pokémon betroffen sind.

Zu Beginn eines eigenen Wildkampfs zeigt der automatische **Catch-Check** unter der Karte das gegnerische Pokémon und das Ergebnis. SoulLink prüft die Evolutionsfamilie und ob der Ort für dich noch frei ist. Die Meldung lässt sich während des Spiels lesen, ohne die Steuerung zu unterbrechen.

Unter der Karte erscheinen die **Live-Kämpfe** deiner Mitspieler. Dort siehst du die aktiven Pokémon und ihre KP. Bei einem beobachteten Treffer blitzt die Karte des betroffenen Pokémon kurz auf und zeigt den KP-Verlust.

Mitspieler desselben Runs erscheinen automatisch als animierte Trainerfiguren im Rubin-Spielfeld, sobald ihr auf derselben Karte und in Sichtweite seid. Das Spiel zeichnet sie mit seiner eigenen Tiefensortierung, etwa hinter Baumkronen oder unter Brücken. Du kannst durch sie hindurchlaufen. Die Figuren verschwinden bei einem Kartenwechsel, im Kampf oder wenn die Verbindung abbricht. Die Anzeige braucht die aktualisierte `SoulLink.exe` bei beiden Spielern und einen Run-Server mit Avatar-Unterstützung.

Beim Run-Beitritt wählst du neben deinem Namen und Karten-Icon auch deine Spielerfarbe. Namen, Icon und Farbe kannst du während des Runs unter **SoulLink → Spielerprofil bearbeiten** ändern. Du kannst eine der vorgegebenen Farben wählen oder einen eigenen Hex-Farbwert eintragen. Im Spielfeld erscheint über deinen Mitspielern jeweils ein farbiges Pixel-Namensschild mit bis zu zwölf Zeichen. Unter **SoulLink → Namensschilder ausblenden** kannst du sie für deine Ansicht abschalten. Wo der Emulator eine Farbpalette frei hat, ersetzt er die roten Kleidungsfarben deiner Spielfigur und der Mitspieler durch die jeweilige Spielerfarbe. Die Hoenn-Karte zeigt beim Darüberfahren die Ortsnamen und freigeschalteten Landmarken aus dem PokéNav. Beim Spielerpunkt siehst du zusätzlich den Namen.

### Poké-Tausch

Öffne **Poké-Tausch** im laufenden Run. Das Grid zeigt lebende Pokémon aus Team und Box. Klicke zuerst auf dein Pokémon und wähle dann in derselben markierten Zeile das Pokémon eines Mitspielers. Ein erneuter Klick auf dein Pokémon hebt die Auswahl auf. **Anfrage senden** öffnet beim Mitspieler ein Popup mit dem Link und beiden Pokémon.

Nach der Zustimmung warten beide Emulatoren auf das Spielfeld. Rubin führt dann seine Tauschanimation aus und entwickelt empfangene Pokémon nach den Regeln der jeweiligen ROM. Das empfangene Pokémon übernimmt den Team- oder Box-Platz des abgegebenen Pokémon. Eier und Pokémon mit Briefen sind vom Tausch ausgenommen.

SoulLink legt pro Tausch lokale Sicherungspunkte an und übernimmt den Tausch in den Link, sobald beide Spielstände bestätigt sind. Nach einer Unterbrechung setzt ein erneuter Start über **Run fortsetzen** den Tausch aus dem Sicherungspunkt fort.

Bei einem Erstencounter sieht nur der kämpfende Spieler die Art im Spiel und im Catch-Check. Für Mitspieler erscheint der Gegner als **Unbekannt** mit seinen KP. Der Fang selbst enthüllt das Pokémon im gemeinsamen Feed.

Für einen Mehrspieler-Test am selben PC öffnest du in **SoulLink → Testspieler öffnen** weitere Fenster. Jedes Testfenster hat eigene Profile und einen eigenen mGBA-Spielstand. Du kannst dieselbe Ausgangs-ROM auswählen. Die Testfenster verwenden getrennte Kopien und übernehmen keine vorhandenen Spielstände. Tritt in jedem Fenster mit einem eigenen Namen derselben Einladung bei und starte danach den Run als Gastgeber.

Der Geldstand steht in der Spielerliste unter der Karte neben dem Ort. Wenn ein Spieler offline ist, zeigt die Liste den zuletzt übertragenen Stand.

Im **Cheat-Menü** stehen Sonderbonbons und alle zwölf Bälle aus Rubin mit Bild und Namen. Wähle oben **Item-Lager** oder **Rucksack** und klicke auf **+99**. Bei jedem Öffnen ist **Item-Lager** vorausgewählt.

Versionen mit eingebautem Updater prüfen beim Öffnen auf neue Releases. Steht eine bereit, findest du unter **SoulLink** den Eintrag **Update … installieren**. Vor der Bestätigung speicherst du in mGBA. SoulLink lädt das signierte Paket, beendet das eigene mGBA und öffnet danach die neue Version. Einladungen, Run-Auswahl und ROM-Pfade bleiben in deinem bisherigen Spielerprofil gespeichert.

Klicke im Team, in der Box, unter **Tot**, **Shinies** oder im Live-Kampf auf ein Pokémon, um seine Werte, Level-Attacken und Entwicklungsmöglichkeiten zu sehen. SoulLink liest diese Tabellen aus der gewählten Rubin-ROM und berücksichtigt dadurch Änderungen des Randomizers. Bei einem Mitspieler fragt SoulLink dessen Spielversion an. Wenn sie gerade unerreichbar ist, kennzeichnet die App deine lokale ROM als Referenz.

Die nächste Arena richtet sich nach dem gemeinsamen Fortschritt. **Abschließen** und **Zurücknehmen** gelten für alle Spieler im Run. Für erkannte Arenateams zeigt SoulLink die Level aus der jeweils geladenen Rubin-ROM. Für andere ROM-Varianten gelten die Rubin-Standardlevel.

Kehre vor dem Hinzufügen von Items aufs Spielfeld zurück. Das Cheat-Menü zeigt danach den neuen Bestand am gewählten Ziel. Speichere wie gewohnt im Spiel.

**Regeln** zeigt jede Grundregel einzeln und stellt eure eigenen Regeln als Karten dar. Alle angemeldeten Teilnehmer können in einem laufenden Run Zusatzregeln hinzufügen, bearbeiten und löschen. Fahr mit der Maus über eine Karte oder wähle sie mit der Tab-Taste, um die Aktionen zu sehen. Im lokalen Spieltest speichert SoulLink Zusatzregeln mit dem lokalen Verlauf.

**Effekte** erklärt die Statusregeln von Pokémon Rubin, einschließlich Initiative und Angriffsausfall bei Paralyse, Rundenschaden und Adrenalin bei Verbrennung sowie normalem und schwerem Gift. Die Übersicht enthält außerdem vorübergehende Kampfeffekte und Wertestufen. [Rubin-Kampfmechanik](https://github.com/pret/pokeruby/blob/master/src/battle_util.c) · [Schadensformel](https://github.com/pret/pokeruby/blob/master/src/calculate_base_damage.c) · [Fangformel](https://github.com/pret/pokeruby/blob/master/src/battle_script_commands.c)

Sobald die Lesebrücke läuft, schließt sich das Scripting-Fenster automatisch. Die Spieldaten werden grün, sobald ihr im Spielfeld seid. Ein leeres Team ist dafür in Ordnung. Gespeichert wird wie gewohnt im Emulator. Der lokale Spieltest startet seinen Feed beim ersten gültigen Spielstand. Die allgemeine Spielerdatei zeigt den lokalen Feed auch bei einer gespeicherten Anmeldung, solange der Run-Server nicht verbunden ist. Die lokale Erfassung gilt nur für diese Sitzung. Sobald der Server wieder erreichbar ist, wechselt die Anzeige zum gemeinsamen Run. Dort erscheinen Fänge, sobald der Gastgeber den Run gestartet hat.

Neue Runs zeigen einen erkannten K. o. bereits im Kampf an. Das Popup öffnet sich ohne Tastaturfokus. Darin kannst du den Tod bestätigen und einen Grund angeben. SoulLink zeigt den Gegner und die K.-o.-Attacke, wenn die Lesebrücke den direkten Treffer zuordnen konnte. In anderen Fällen zeigt es die zuletzt erfasste Attacke, sofern bekannt. Die Bestätigung markiert den gesamten Link als tot. **Später** hält den Fall unter **Todesfall prüfen** offen.

Für einen ROM-Wechsel schließt du mGBA und gehst zu **SoulLink → Einrichtung → ROM wechseln**. Unter **Verbindung** findest du den Status von mGBA und Run-Server und das Diagnoseprotokoll.

## Anmeldung am gemeinsamen Run

Für einen neuen Run bekommst du eine Einladung als Text. Derselbe Dialog öffnet auch gespeicherte Runs. Die Einladung enthält die Serveradresse und den Run-Namen. SoulLink schlägt deinen bisherigen Namen und dein Icon vor, wenn du schon in einem Run angemeldet bist. Die Anmeldung und ROM-Auswahl werden für jeden Run einzeln gespeichert.

## Für Gastgeber

Für einen Server ohne Discord öffnest du **SoulLink → Gastgeber**. Gib die HTTPS-Adresse und den Admin-Schlüssel ein und klicke auf **Verbinden**. Wähle eine Gruppe oder lege eine neue an. Links kannst du einen Run anlegen oder auswählen. Rechts stehen die Aktionen zum ausgewählten Run. Über **Einladung kopieren** erhältst du den Text zum Weitergeben. SoulLink merkt sich dabei die Einladung. **Selbst beitreten** öffnet den Run direkt auf deinem Rechner.

Für alte Discord-Runs wählst du in der Gastgeberverwaltung die Zielgruppe und klickst auf **Discord-Runs importieren**. Wähle die `runs.json` oder ihre `.json.bak` bis 20 MB. Die importierten Runs erscheinen direkt in der Run-Liste der Gruppe. Dort kannst du auch abgeschlossene Runs auswählen und bei Discord-Archiven Spiel, Startdatum, Enddatum und einen vorhandenen Endgrund nachsehen. Über **Run laden → Alle Runs der Gruppe** kommst du ebenfalls in diese Übersicht.

Der Server bewahrt die Originaldaten in SQLite auf. Discord-Archive sind schreibgeschützt. Ein erneuter Import in dieselbe Gruppe überspringt vorhandene Runs. Enthält die Datei einen bereits einer anderen Gruppe zugeordneten Run, wird der gesamte Import abgelehnt.

## Gruppenstatistik

Öffne in einem angemeldeten Run-Profil oben **Statistik**. Die Ansicht lädt auch bei geschlossenem Spiel und enthält alle Runs derselben Gruppe, einschließlich abgeschlossener SoulLink-Runs und importierter Discord-Runs. Wenn eine Gruppe bisher nur Discord-Archive enthält, kannst du ihre Statistik über **Gastgeber → Gruppe wählen → Statistik** öffnen. Spieler erhalten Zugriff, sobald sie einem SoulLink-Run dieser Gruppe beigetreten sind.

Die Übersicht zeigt gewertete Fänge ohne Starter, als tot markierte Pokémon einschließlich Linkpartner und den höchsten gemeinsamen Arena-Fortschritt. Wähle einen Run aus, um sein Datum und einen vorhandenen Endgrund zu sehen. Ein Gedankenstrich steht für ein fehlendes Archivfeld. Die Summen zählen die erfassten Werte.

Die Diagramme zeigen Fänge, Pokémon-Tode und Arenen in chronologischer Run-Reihenfolge. Bewege den Mauszeiger über einen Punkt, um Run, Datum, Quelle und Wert zu sehen. Mit der Tabulatortaste und den Pfeiltasten kannst du die Punkte auch per Tastatur erkunden. Fehlende Archivwerte erscheinen als Lücken.

Über **Statistik → Timeline** spielst du die Ereignisse aller Runs der Gruppe gleichzeitig ab. Jeder Run hat eine eigene Spur. Pro Schritt erscheint je Run das nächste erfasste Ereignis. Mit dem Regler oder einem Klick auf einen Punkt springst du zu einem Schritt. Beim Überfahren eines Punkts siehst du den gespeicherten Zeitpunkt und das Ereignis.

Beginnt ihr gemeinsam ein neues Spiel, kannst du den Run starten, sobald alle im Spielfeld angekommen sind. SoulLink erfasst die frisch gewählten Starter automatisch. Mit **Links bearbeiten** kannst du Starter und Fänge im aktiven oder pausierten Run nachträglich zuordnen. Ziehe jedes Pokémon in seiner Spielerspalte zur gewünschten Zeile oder zu **Unverlinkt**. **Links speichern** übernimmt die Zuordnung für alle. Der Starter-Link lässt Route 101 für einen regulären Fang frei. Im Grid steht **Starter** auf den zugeordneten Pokémon. Beim Darüberfahren siehst du den ursprünglichen Fangort.

Unter **SoulLink → Run beitreten / wechseln** findest du die gemerkten Einladungen. Beim Wechsel bitte vorher im Spiel speichern. SoulLink beendet mGBA und öffnet dieselbe EXE mit dem gewählten Run-Profil.

### Einladungen per Discord

Füge den Einladungscode aus `/run-init` unter **SoulLink → Run beitreten / wechseln** ein und gib die Serveradresse im eingeblendeten Feld an. Die Einladung gilt für die noch offene Lobby.

Für den Betrieb mit einem laufenden manuellen Run gelten weiterhin die Hinweise der [Server-Anleitung](README.md): einen getrennten Test-Bot und eine eigene Testdatenbank benutzen. Der neue Client übernimmt keine bestehenden manuellen Links nachträglich.

### Zu zweit ohne Discord testen

1. Verbinde dich unter **SoulLink → Gastgeber** mit eurem HTTPS-Run-Server und lege eine Testgruppe mit einem Test-Run an.
2. Gib beiden Spielern einmal die SoulLink.exe. Schick ihnen die Einladung aus **Einladung kopieren**.
3. Beide fügen die Einladung unter **Run beitreten / wechseln** ein und wählen Name, Karten-Icon und lokale Rubin-ROM. Sobald beide im Spielfeld sind, klickt der Gastgeber auf **Aktualisieren** und beim gewählten Run auf **Starten**.
4. Fangt beide ein Pokémon im gleichen Fanggebiet. Der Link erscheint auf beiden Rechnern. Fügt unter **Regeln** eine Testregel hinzu und prüft, ob sie beim anderen Spieler erscheint.

Für einen automatischen Test der Mehrspieler-Verbindung auf einem Rechner gibt es `./scripts/Start-SoulLinkDemo.ps1 -Verify`.

Aktuell steht das [Profil für deutsches Rubin](mgba.md) zur Verfügung. SoulLink verwendet die flachen Sammelkarten-Symbole und ergänzt für Typen wie Boden, Gestein und Flug die eigenen Spielsymbole aus Pokémon Legenden: Arceus. Jeder Typ hat ein eigenes Symbol und einen deutschen Tooltip. Heruntergeladene Bilder sind danach auch offline verfügbar. Die Icons des Discord-Bots sind in der EXE enthalten.

Der lokale Spieltest speichert seinen Feed und bestätigte Tode in einer privaten SQLite-Datei pro ROM und Trainer. Bereits erfasste Fänge behalten beim Schnellladen und Neustarten ihre ursprüngliche Uhrzeit. Aus dem Spielstand lässt sich der aktuelle Stand von Team und Box lesen. Fangzeiten und bestätigte SoulLink-Tode stammen aus der Run-Historie.
