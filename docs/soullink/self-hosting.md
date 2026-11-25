# SoulLink auf einem eigenen Linux-Server betreiben

Diese Anleitung verwendet Linux x86-64 mit systemd, eine eigene Domain und Caddy als HTTPS-Proxy. Der Server läuft auch ohne Discord. Die Windows-Spielerdatei kann für jeden Run wiederverwendet werden. Die vom Server benötigten Systembibliotheken hängen von der Linux-Distribution ab. [Microsoft zu nativen Abhängigkeiten](https://learn.microsoft.com/en-us/dotnet/core/install/linux-scripted-manual#dependencies)

## 1. Paket auf den Server übertragen

Verwende `soullink-linux-x64.tar.gz` aus dem Server-Build. Wenn du selbst baust, erzeugt `pwsh ./scripts/Publish-SoulLinkServer.ps1` das Paket unter `artifacts/server/` und gibt die SHA-256-Prüfsumme aus. Das Paket enthält den Server mit .NET-Laufzeit, den systemd-Dienst, das Update-Skript und Vorlagen für die Konfiguration.

Übertrage das Archiv per SSH/SFTP auf den Linux-Server. Vergleiche die Ausgabe von `sha256sum soullink-linux-x64.tar.gz` mit der Prüfsumme des Herausgebers. Die folgenden Befehle werden auf dem Server im Verzeichnis mit dem Archiv ausgeführt. `tar`, `curl`, `flock`, `openssl` und `sudo` müssen verfügbar sein.

```bash
archive=$(realpath soullink-linux-x64.tar.gz)
setup=$(mktemp -d)
tar -xzf "$archive" -C "$setup"
```

## 2. Dienst und ersten Admin-Schlüssel einrichten

Diese Einrichtung ist für eine neue Installation. Bei späteren Updates mit dem Abschnitt [Server aktualisieren](#server-aktualisieren) fortfahren, damit die bereits eingerichteten Zugangsdaten und Einstellungen erhalten bleiben.

```bash
id -u soullink >/dev/null 2>&1 || sudo useradd --system --user-group \
  --home-dir /var/lib/soullink --shell /usr/sbin/nologin soullink
sudo install -d -m 0755 /etc/soullink
sudo install -m 0644 "$setup/deploy/soullink.service" /etc/systemd/system/soullink.service
sudo test -e /etc/soullink/server.env || sudo install -m 0600 "$setup/deploy/server.env.example" /etc/soullink/server.env
openssl rand -hex 32
sudoedit /etc/soullink/server.env
```

Trage die Ausgabe von `openssl` hinter `SoulLink__AdminKey=` ein und speichere sie in deinem Passwortmanager. Der Schlüssel verwaltet den gesamten Server. Im optionalen Discord-Betrieb setzt du zusätzlich `SoulLink__EnableDiscordEvents=true` und trägst `DISCORD_BOT_TOKEN` für deinen eigenen Bot auf dem Server ein.

```bash
sudo systemctl daemon-reload
sudo bash "$setup/deploy/Update-SoulLinkServer.sh" "$archive"
sudo systemctl enable soullink.service
curl --fail http://127.0.0.1:5080/health/soullink
```

Der Dienst läuft als `soullink`. Releases liegen unter `/opt/soullink/releases/`, der aktive Stand unter `/opt/soullink/current`. Die SQLite-Datenbank und Backups liegen unter `/var/lib/soullink/`. Bei einem Startfehler zeigt `sudo journalctl -u soullink.service -n 50` die letzten Meldungen.

## 3. Eigene HTTPS-Domain verbinden

Lege einen DNS-Eintrag für deine Domain an und leite die öffentlichen Ports 80 und 443 an Caddy. Der Port 5080 gehört zum Loopback-Dienst auf dem Server. Installiere Caddy nach der [offiziellen Anleitung](https://caddyserver.com/docs/install) und übernimm den Block aus `deploy/Caddyfile.example` in `/etc/caddy/Caddyfile`. Ersetze `soullink.example.org` durch deine Domain. Caddy übernimmt TLS und leitet auch den SignalR-WebSocket weiter. [Caddy zu HTTPS](https://caddyserver.com/docs/quick-starts/https) · [Caddy zu WebSockets](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy)

```bash
sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
sudo systemctl reload caddy
curl --fail https://soullink.example.org/health/soullink
```

Führe die letzte Prüfung auch von einem anderen Rechner aus. Die Beispieladresse steht für deine eigene Domain.

## 4. Ersten Run anlegen und Freunde verbinden

1. Öffne die allgemeine `SoulLink.exe` und wähle **SoulLink → Gastgeber**. Gib deine HTTPS-Adresse und deinen Admin-Schlüssel ein.
2. Lege eine Gruppe und einen Run an. Wähle **Einladung kopieren** und sende deinen Freunden den Text und die allgemeine `SoulLink.exe`.
3. Deine Freunde öffnen **Run beitreten / wechseln**, fügen die Einladung ein und wählen ihre lokale Rubin-ROM. Die Einladung übermittelt die Serveradresse. Sobald alle angemeldet und im Spiel sind, klicke als Gastgeber auf **Starten**.

## Server aktualisieren

Übertrage das neue Serverarchiv, prüfe seine Prüfsumme und entpacke es wie in Schritt 1 in ein neues temporäres Verzeichnis. Führe nur das Update-Skript aus.

```bash
sudo bash "$setup/deploy/Update-SoulLinkServer.sh" "$archive"
curl --fail http://127.0.0.1:5080/health/soullink
```

Das Skript erstellt bei vorhandener Datenbank ein SQLite-Backup unter `/var/lib/soullink/backups/`, migriert das Schema und prüft den gestarteten Dienst. Das vorherige Release bleibt für einen gezielten Rollback verfügbar. Wenn der Dienst nach der Migration fehlschlägt, prüfe das Journal und das passende Backup vor einer Wiederherstellung. Sichere Zustandsdaten und `/etc/soullink/server.env` auch regelmäßig außerhalb des Servers.

## Optionale automatische Windows-Updates

Die allgemeine Spielerdatei genügt für die Einladungen und manuelle Updates. Für automatische Updates baut der Herausgeber auf Windows einen eigenen Player mit expliziter HTTPS-Update-Adresse und separatem Signaturschlüssel. Archiv und Manifest werden über öffentliche Download-Endpunkte angeboten. Für eine private Runde kann der Betreiber die EXE über eine kontogebundene Freigabe verteilen.

```powershell
$releaseKey = Join-Path $env:LOCALAPPDATA 'SoulLink\ReleaseSigning\own-server.dpapi'
./scripts/Publish-SoulLinkPlayerUpdate.ps1 -Version 1.0.0 `
    -Server 'https://soullink.example.org' -KeyFile $releaseKey
```

Der erste Build erzeugt den Windows-benutzergebundenen Schlüssel bei Bedarf. Behalte ihn für spätere Updates derselben Installation bei und sichere den Zugang zum Windows-Konto. Übertrage `artifacts/server/soullink-player-release.zip` auf den Server und installiere es mit `sudo python3 /opt/soullink/current/Install-SoulLinkPlayerRelease.py /pfad/zum/soullink-player-release.zip`. Für die Spieler verteilt der Herausgeber einmal die dazugehörige `SoulLink.exe` aus `artifacts/player/`. Der private Signaturschlüssel bleibt beim Herausgeber. Die Updatesignatur prüft das heruntergeladene Paket innerhalb von SoulLink und ist unabhängig von Windows Authenticode.
