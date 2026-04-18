# Entwicklungsrichtlinien

Diese Richtlinien gelten fuer alle Entwicklungsaufgaben in diesem Repository.

- Vor Beginn eines Tickets wird geprueft, ob es bereits einen passenden Branch gibt. Falls nicht, wird von `main` aus ein neuer Feature-Branch erstellt und vorher der aktuelle Stand von `origin/main` geholt.
- Aenderungen fuer ein Ticket bleiben auf dem zugehoerigen Branch. Nach Abschluss werden die relevanten Dateien geprueft, committed und auf den Branch gepusht.
- Git-Befehle fuer den normalen Ticket-Workflow duerfen ohne Rueckfrage ausgefuehrt werden, insbesondere `git add`, `git commit`, `git switch` und `git push` ohne Force-Optionen. Force-Pushes, auch `--force-with-lease`, duerfen nur nach ausdruecklicher Zustimmung ausgefuehrt werden.
- Falls fuer den Task noch kein Pull Request existiert, wird nach dem Push ein Pull Request erstellt. Wenn die Aufgabe aus Sicht von Codex abgeschlossen ist, darf der Pull Request nicht im Draft-Modus bleiben.
- Pull Requests werden mit aussagekraeftigem Titel, nachvollziehbarer Beschreibung, passenden Labels und, soweit bekannt, sinnvollen Reviewern oder weiteren Metadaten versehen.
- StyleCop-Anmerkungen werden immer behoben. Neuer oder geaenderter Code soll keine StyleCop-Warnungen einfuehren.
- Code wird mit Clean-Code-Prinzipien im Blick geschrieben: klare Namen, kleine fokussierte Einheiten, geringe Kopplung und gut lesbare Kontrollfluesse.
- SOLID-Prinzipien werden beachtet. Abhaengigkeiten, Verantwortlichkeiten und Erweiterungspunkte sollen bewusst geschnitten sein.
- Code soll getestet sein. Neue Logik bekommt passende Tests; bestehende Tests werden angepasst, wenn sich Verhalten bewusst aendert.
- Vor einem Commit wird der Diff geprueft, insbesondere auf unbeabsichtigte Formatierungs-, Whitespace- oder Line-Ending-Aenderungen. Line Endings muessen dem im Repository gesetzten Standard entsprechen.
- Manuelle Datei-Aenderungen werden direkt mit CRLF geschrieben oder unmittelbar nach dem Patch auf CRLF normalisiert. Vor dem Commit wird mit `git ls-files --eol` geprueft, dass geaenderte Textdateien nicht mit gemischten Line Endings im Worktree liegen.
- Vor Abschluss einer Aufgabe werden relevante Formatierungs-, Analyse- und Testbefehle ausgefuehrt, soweit sie im Projekt verfuegbar und praktikabel sind.
- Alle relevanten Tests muessen vor Abschluss gruen sein. Falls ein Test nicht ausgefuehrt werden kann, wird der Grund dokumentiert.
- Bestehende Nutzer- oder Fremdaenderungen werden nicht ueberschrieben oder zurueckgesetzt, ausser dies wurde ausdruecklich beauftragt.
- Commits sollen fokussiert und nachvollziehbar sein. Commit- und PR-Beschreibungen nennen kurz Zweck, wichtigste Aenderungen und ausgefuehrte Verifikation.
