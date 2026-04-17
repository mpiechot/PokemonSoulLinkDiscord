# Entwicklungsrichtlinien

Diese Richtlinien gelten fuer alle Entwicklungsaufgaben in diesem Repository.

- Vor Beginn eines Tickets wird geprueft, ob es bereits einen passenden Branch gibt. Falls nicht, wird von `main` aus ein neuer Feature-Branch erstellt und vorher der aktuelle Stand von `origin/main` geholt.
- Aenderungen fuer ein Ticket bleiben auf dem zugehoerigen Branch. Nach Abschluss werden die relevanten Dateien geprueft, committed und auf den Branch gepusht.
- Falls fuer den Task noch kein Pull Request existiert, wird nach dem Push ein Pull Request erstellt.
- StyleCop-Anmerkungen werden immer behoben. Neuer oder geaenderter Code soll keine StyleCop-Warnungen einfuehren.
- Code wird mit Clean-Code-Prinzipien im Blick geschrieben: klare Namen, kleine fokussierte Einheiten, geringe Kopplung und gut lesbare Kontrollfluesse.
- SOLID-Prinzipien werden beachtet. Abhaengigkeiten, Verantwortlichkeiten und Erweiterungspunkte sollen bewusst geschnitten sein.
- Code soll getestet sein. Neue Logik bekommt passende Tests; bestehende Tests werden angepasst, wenn sich Verhalten bewusst aendert.
- Vor einem Commit wird der Diff geprueft, insbesondere auf unbeabsichtigte Formatierungs-, Whitespace- oder Line-Ending-Aenderungen. Line Endings muessen dem im Repository gesetzten Standard entsprechen.
- Vor Abschluss einer Aufgabe werden relevante Formatierungs-, Analyse- und Testbefehle ausgefuehrt, soweit sie im Projekt verfuegbar und praktikabel sind.
- Alle relevanten Tests muessen vor Abschluss gruen sein. Falls ein Test nicht ausgefuehrt werden kann, wird der Grund dokumentiert.
- Bestehende Nutzer- oder Fremdaenderungen werden nicht ueberschrieben oder zurueckgesetzt, ausser dies wurde ausdruecklich beauftragt.
- Commits sollen fokussiert und nachvollziehbar sein. Commit- und PR-Beschreibungen nennen kurz Zweck, wichtigste Aenderungen und ausgefuehrte Verifikation.
