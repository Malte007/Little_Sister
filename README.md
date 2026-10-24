# Little Sister

Selbst gehosteter Dienst für Überwachungskameras: Kameras verwalten, Aufnahmen in einem Ringspeicher ablegen und über eine Zeitleiste mit Ereignismarkern abspielen.

- Backend: ASP.NET Core (.NET 10), EF Core mit SQLite
- Frontend: SvelteKit + TypeScript (als statische Single-Page-App vom Backend ausgeliefert)
- Live-Ansicht: go2rtc (WebRTC, MSE als Rückfall), von Little Sister selbst gestartet
- Betrieb: Ubuntu-Server als systemd-Dienst, alternativ Docker

## Stand

| Bereich | Status |
| --- | --- |
| Ersteinrichtung (Admin, Speicherort, Sprache, Zeitzone) | fertig |
| Login per Cookie (Web) und Token (Apps), Rollen Admin/Betrachter | fertig |
| Benutzerverwaltung | fertig |
| Kameras per ONVIF-Suche oder RTSP-Adresse, Zugangsdaten verschlüsselt | fertig |
| Daueraufnahme (ein ffmpeg-Prozess pro Kamera, Segmente ohne Umwandlung, Neustart bei Ausfall) | fertig |
| Ringspeicher (Kontingent pro Kamera, maximale Aufbewahrung, globale Freispeichergrenze) | fertig |
| Live-Ansicht (Kachelraster, Einzelansicht mit voller Auflösung und Ton) | fertig |
| Kamerastatus live in der Oberfläche (SignalR) | fertig |
| Zeitleiste und Wiedergabe (Tag wählen, springen, 1×–8×), Export eines Zeitraums als MP4 | fertig |
| Vorschaubilder auf der Zeitleiste, mehrere Kameras gleichzeitig abspielen | fertig |
| Aufnahme bei Bewegung (Erkennung im Bild oder per ONVIF, Vor- und Nachlauf, Bewegungen auf der Zeitleiste) | fertig |

## Installation auf einem Ubuntu-Server

Little Sister läuft als systemd-Dienst direkt auf dem Server. Das Skript `scripts/install-ubuntu.sh` baut Oberfläche und Backend, installiert go2rtc, legt den Dienstbenutzer `littlesister` an und richtet den Dienst ein. Für Updates wird es einfach erneut aufgerufen; Datenbank, Aufnahmen und Einstellungen bleiben erhalten.

Das Skript braucht .NET 10 SDK, ffmpeg und Node.js ab 22.12 (zum Bauen der Oberfläche). Fehlt davon etwas, listet es die fehlenden Programme auf und installiert sie nach Rückfrage: .NET 10 und ffmpeg per apt (bei älteren Ubuntu-Versionen über `ppa:dotnet/backports`, ebenfalls nach Rückfrage), Node.js 24 über die Paketquelle von NodeSource. Vorher wird nur git zum Herunterladen gebraucht:

```fish
sudo apt update
sudo apt install -y git
```

| Zeile | Wirkung |
| --- | --- |
| `sudo apt update` | lädt die aktuellen Paketlisten |
| `sudo apt install -y git` | installiert git (auf Ubuntu-Server meist schon vorhanden) |

Projekt holen und installieren:

```fish
git clone https://github.com/malte007/little_sister.git
cd little_sister
./scripts/install-ubuntu.sh
```

| Zeile | Wirkung |
| --- | --- |
| `git clone …` | lädt das Repository herunter (privat: mit GitHub-Benutzer und Token anmelden) |
| `cd little_sister` | wechselt in den Projektordner |
| `./scripts/install-ubuntu.sh` | baut alles, installiert nach `/opt/little-sister` und startet den Dienst; fragt nach dem sudo-Passwort |

Bei der Erstinstallation fragt das Skript nach dem Port für Oberfläche und API (Enter = 8080) und prüft, ob er frei ist. Ohne Rückfrage geht es mit `env HTTP_PORT=9000 ./scripts/install-ubuntu.sh`. Bei Updates wird nicht erneut gefragt, der eingestellte Port bleibt.

Port später ändern:

```fish
sudo nano /etc/little-sister/little-sister.env
sudo systemctl restart little-sister
```

| Zeile | Wirkung |
| --- | --- |
| `sudo nano …` | öffnet die Einstellungen; dort `ASPNETCORE_HTTP_PORTS=8080` auf den neuen Port setzen |
| `sudo systemctl restart little-sister` | startet den Dienst mit dem neuen Port neu |

Danach `http://<server>:<port>` öffnen (Standard 8080); der Einrichtungsassistent startet automatisch. Als Aufnahmeordner ist `/srv/little-sister/recordings` vorgeschlagen. Ein anderer Ordner muss dem Benutzer `littlesister` gehören (`sudo chown -R littlesister: <ordner>`).

| Was | Wo |
| --- | --- |
| Programm | `/opt/little-sister/app` |
| Datenbank, Schlüssel, Sicherungen | `/var/lib/little-sister` |
| Einstellungen (Umgebungsvariablen, siehe unten) | `/etc/little-sister/little-sister.env` |
| Protokoll | `journalctl -u little-sister -f` |

Aktualisieren:

```fish
cd little_sister
git pull
./scripts/install-ubuntu.sh
```

| Zeile | Wirkung |
| --- | --- |
| `cd little_sister` | wechselt in den Projektordner |
| `git pull` | holt den neuesten Stand |
| `./scripts/install-ubuntu.sh` | baut neu und startet den Dienst neu; Datenbank-Migrationen laufen beim Start automatisch, vorher wird die Datenbank gesichert |

Ist eine Firewall (ufw) aktiv, müssen der Port für die Oberfläche (Standard 8080) und 8555 TCP/UDP (WebRTC) freigegeben werden: `sudo ufw allow 8080/tcp`, `sudo ufw allow 8555`.

## Betrieb

### Live-Ansicht

Little Sister startet go2rtc selbst (im Docker-Image enthalten) und hält dessen Streams mit den Kameras in Sync. Die go2rtc-API lauscht nur auf `127.0.0.1`; der Browser bekommt Bild und Standbilder ausschließlich über die angemeldete Little-Sister-API. Die Kacheln nutzen den Substream, die Einzelansicht wahlweise den Hauptstream.

Für WebRTC (Latenz unter einer Sekunde) muss Port **8555 (TCP und UDP)** vom Browser aus erreichbar sein. Ist er es nicht, fällt der Player automatisch auf MSE über die normale Verbindung zurück (etwas mehr Verzögerung). Fehlt go2rtc, ist nur die Live-Ansicht aus, die Aufnahme läuft weiter.

### Wiedergabe und Export

Unter **Aufnahmen** zeigt die Zeitleiste pro Kamera und Tag, wann aufgenommen wurde. Ein Klick springt an die Stelle, Lücken werden beim Abspielen übersprungen. Abgespielt wird per HLS direkt aus den gespeicherten Segmenten, ohne Umwandlung und ohne CPU-Last auf dem Server. Der Export setzt den markierten Zeitraum ebenfalls ohne Umwandlung zu einer MP4-Datei zusammen; er beginnt am Schlüsselbild vor dem gewählten Anfang. Tagesgrenzen und Uhrzeiten richten sich nach der Zeitzone der Installation.

Über **+ Kamera hinzufügen** laufen bis zu neun Kameras synchron über dieselbe Zeitleiste, je Kamera mit einer eigenen Spur. Über die Auswahl im Kachelkopf lässt sich eine Kamera gegen eine andere tauschen, ✕ entfernt sie. Hat eine Kamera zur aktuellen Uhrzeit keine Aufnahme, pausiert sie, bis wieder etwas aufgenommen wurde. Der Export erzeugt je Kamera eine eigene Datei. Jede Kamera dekodiert ihr eigenes Video im Browser; bei vielen 4K-Kameras kann ein schwächerer Rechner an seine Grenzen kommen.

Fährt die Maus über die Zeitleiste, erscheint ein Vorschaubild. Dafür legt Little Sister zu jedem Segment einen Bildstreifen mit einem Bild alle 10 Sekunden ab. Der Streifen entsteht nach der Aufnahme im Hintergrund aus den Schlüsselbildern, also mit wenig Rechenzeit. Für ältere Aufnahmen entsteht er beim ersten Überfahren. Wer die Rechenzeit sparen will, schaltet die Vorschaubilder unter **Kameras → Speicher** aus; dann entstehen keine neuen Bilder, und die Zeitleiste zeigt keine Vorschau.

### Aufnahme bei Bewegung

Je Kamera lässt sich unter **Kameras → Bearbeiten** die Aufnahmeart wählen: **Dauerhaft**, **Bei Bewegung** oder **Aus**. Bei **Bei Bewegung** gibt es zwei Arten, Bewegung zu erkennen:

| Erkennung | Wie | Voraussetzung |
| --- | --- | --- |
| **Im Bild** (Standard) | Ein eigener ffmpeg-Prozess dekodiert den Substream und liefert 5 Bilder/s, verkleinert auf 80×60 Graustufen. Little Sister zählt die Pixel, die deutlich vom gleitenden Hintergrund abweichen. Ab einem Mindestanteil (Regler **Empfindlichkeit**: 1 = 10 % des Bildes, 50 ≈ 1,4 %, 100 = 0,2 %) beginnt die Bewegung, nach 3 s Ruhe endet sie. Ändert sich fast das ganze Bild auf einmal (Licht, Nachtsicht), zählt das nicht. | jede Kamera; Substream empfohlen. CPU-Last gemessen: etwa 3 % eines Kerns bei 640×360, etwa 6 % bei 1280×720 (ohne Substream wird der Hauptstream dekodiert, das kostet mehr) |
| **Von der Kamera (ONVIF)** | Die Kamera erkennt selbst und meldet per ONVIF-Ereignis. | ONVIF-Adresse (wird bei der Suche eingetragen; `http://IP:Port` genügt) und eingeschaltete Erkennung in der Kamera. Viele günstige Kameras setzen das nur teilweise um. |

Im erweiterten Modus (**Nur in bestimmten Bereichen erkennen**) wird das Bild in 16×12 Felder geteilt. Auf dem Standbild der Kamera malst du per Klicken und Ziehen die Felder an, in denen Bewegung zählt; alles andere (Straße, Baum, Nachbargrundstück) wird ignoriert. Die Empfindlichkeit bezieht sich dann auf die überwachte Fläche.

| Vorlauf | Was passiert |
| --- | --- |
| 0 Sekunden | ffmpeg startet erst mit der Bewegungsmeldung und hört nach dem Nachlauf wieder auf. Ohne Bewegung läuft nichts. Bis die Verbindung steht, vergehen etwa 1 bis 3 Sekunden; dieser Anfang fehlt in der Aufnahme. |
| mehr als 0 | Der Stream läuft dauernd in einen kurzen Zwischenspeicher (`.buffer`), der laufend gelöscht wird. Kommt eine Bewegung, wird das Stück davor übernommen. Gespeichert wird in Stücken etwa so lang wie der Vorlauf, daher kann ein paar Sekunden mehr übrig bleiben. |

Der Nachlauf (0 bis 600 Sekunden) bestimmt, wie lange nach dem Ende der Bewegung weiter aufgenommen wird. Bewegungen erscheinen auf der Zeitleiste als gelbe Balken über der Aufnahme; mit **‹ Bewegung** und **Bewegung ›** springt die Wiedergabe zur vorherigen oder nächsten. Im Kamerastatus zeigt **Wartet auf Bewegung**, dass gerade nichts gespeichert wird, und eine gelbe Marke **Bewegung**, dass die Kamera gerade Bewegung meldet. Bei Daueraufnahme werden Bewegungen nur bei Erkennung per ONVIF auf der Zeitleiste angezeigt; die Erkennung im Bild läuft nur im Modus „Bei Bewegung“, um Rechenzeit zu sparen.

Bei Erkennung per ONVIF holt Little Sister die Meldungen bevorzugt selbst bei der Kamera ab (PullPoint). Kann eine Kamera das nicht (Fehler „ActionNotSupported“), schickt sie ihre Meldungen per HTTP an Little Sister (WS-BaseNotification) an `http://<server>:<port>/api/onvif/events/<schlüssel>`. Dafür muss die Kamera den Server erreichen: Der HTTP-Port (8080, in der Entwicklung 5080) darf von der Firewall nicht gesperrt sein, und Little Sister darf nicht nur auf `localhost` lauschen. Die Serveradresse ermittelt Little Sister selbst. Hinter NAT oder in Docker mit Bridge-Netz lässt sie sich mit `LittleSister__Events__CallbackUrl` vorgeben.

### Ablage der Aufnahmen

```
recordings/<kamera-id>/<JJJJMMTT>/<HHMMSS>.mp4   fertige Segmente (Zeiten in UTC)
recordings/<kamera-id>/<JJJJMMTT>/<HHMMSS>.jpg   Vorschaubilder des Segments
recordings/<kamera-id>/.incoming/                 Segment, das gerade geschrieben wird
recordings/<kamera-id>/.buffer/                   Zwischenspeicher für den Vorlauf (nur bei Aufnahme bei Bewegung)
```

Segmente sind fragmentierte MP4-Dateien und lassen sich direkt mit jedem Player öffnen. Der Ringspeicher löscht die Vorschaubilder zusammen mit ihrem Segment.

### Konfiguration

Alle Einstellungen lassen sich per Umgebungsvariable setzen (doppelter Unterstrich trennt Ebenen), auf dem Ubuntu-Server in `/etc/little-sister/little-sister.env`, danach `sudo systemctl restart little-sister`:

| Variable | Standard | Bedeutung |
| --- | --- | --- |
| `LittleSister__DataPath` | `/var/lib/little-sister` (Ubuntu), `/data` (Docker) | Datenbank, Schlüssel, Sicherungen |
| `LittleSister__DefaultRecordingsPath` | `/srv/little-sister/recordings` (Ubuntu), `/recordings` (Docker) | Vorschlag im Einrichtungsassistenten |
| `LittleSister__Database__Provider` | `Sqlite` | weitere Provider folgen |
| `LittleSister__Database__ConnectionString` | SQLite-Datei im Datenordner | eigene Verbindung |
| `LittleSister__KeysPath` | `<DataPath>/keys` | Schlüssel für Cookies und Kamera-Passwörter, getrennt von der DB ablegbar |
| `LittleSister__Recording__SegmentSeconds` | `60` | Länge eines Aufnahmesegments |
| `LittleSister__Recording__FfmpegPath` | `ffmpeg` | Pfad zu ffmpeg (im Image enthalten) |
| `LittleSister__Live__Go2rtcPath` | `go2rtc` | Pfad zu go2rtc (im Image enthalten) |
| `LittleSister__Live__ApiPort` | `1984` | go2rtc-API, nur auf 127.0.0.1 |
| `LittleSister__Live__WebRtcPort` | `8555` | WebRTC-Port (TCP und UDP) |
| `LittleSister__Live__WebRtcCandidates__0` | leer | zusätzliche WebRTC-Adresse, z. B. `192.168.1.10:8555` bei Docker-Bridge oder NAT |
| `LittleSister__Events__CallbackUrl` | automatisch | Adresse, unter der Kameras ohne PullPoint Little Sister erreichen, z. B. `http://192.168.1.10:8080` |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Port im Container |

## Alternativ: Docker (z. B. Unraid)

Auf einem Ubuntu-Server ist die Installation oben der vorgesehene Weg. Für Docker-Hosts wie Unraid gibt es ein fertiges Image.

### Fertiges Image von GitHub (ghcr.io)

Sobald ein Versions-Tag wie `v0.1.1` oder `v0.1.2-test` nach GitHub gepusht wird, baut die CI das Docker-Image und legt es in der **GitHub Container Registry** (ghcr.io) ab, einem Speicher für Docker-Images in deinem GitHub-Konto. Normale Commits auf `main` und Pull Requests werden nur getestet, sie bauen kein Image.

```
ghcr.io/malte007/little_sister:0.1.1       genau diese Version (Tag v0.1.1)
ghcr.io/malte007/little_sister:0.1.2-test  Testversion (Tag mit Zusatz nach "-")
ghcr.io/malte007/little_sister:latest      die neueste fertige Version (Tags ohne Zusatz)
```

Eine Version veröffentlichen geht in Rider über **Git → New Tag…** (Name z. B. `v0.1.1`) und danach **Git → Push…** mit Haken bei **Push Tags**. Im Terminal (fish):

```fish
git tag v0.1.1
git push Github v0.1.1
```

| Zeile | Wirkung |
| --- | --- |
| `git tag v0.1.1` | markiert den aktuellen Commit als Version 0.1.1 |
| `git push Github v0.1.1` | schickt den Tag zu GitHub (`Github` ist der Name deines Remotes); die CI baut dann das Image |

Zu sehen ist es in deinem GitHub-Profil unter **Packages**. Weil das Repository privat ist, ist auch das Paket privat. Ein Server braucht daher eine Anmeldung mit einem Token, bevor er es herunterladen darf.

### Auf Unraid einrichten

**1. Token anlegen.** Auf GitHub: **Settings → Developer settings → Personal access tokens → Tokens (classic) → Generate new token (classic)**. Name z. B. `unraid`, Ablaufdatum nach Wahl, nur den Haken **`read:packages`** setzen. Den Token kopieren, er wird nur einmal angezeigt. (Fein abgestufte Tokens unterstützen die Container Registry nach meinem Stand nicht.)

**2. Unraid bei ghcr.io anmelden.** Im Unraid-Webinterface oben rechts das Terminal **>_** öffnen. Dort läuft bash, nicht fish:

```bash
docker login ghcr.io -u malte007
mkdir -p /boot/config/docker-auth && cp /root/.docker/config.json /boot/config/docker-auth/
echo 'mkdir -p /root/.docker && cp /boot/config/docker-auth/config.json /root/.docker/' >> /boot/config/go
```

| Zeile | Wirkung |
| --- | --- |
| `docker login ghcr.io -u malte007` | meldet Docker bei ghcr.io an; als Passwort den Token einfügen |
| `mkdir … && cp …` | sichert die Anmeldung auf dem USB-Stick, denn `/root` liegt auf Unraid im Arbeitsspeicher und ist nach einem Neustart leer |
| `echo … >> /boot/config/go` | stellt die Anmeldung bei jedem Start von Unraid wieder her (`go` wird beim Hochfahren ausgeführt) |

Die Anmeldung wird nur zum Herunterladen und für „Nach Updates suchen“ gebraucht. Ein laufender Container läuft auch ohne sie weiter.

**3. Container anlegen.** **Docker → Container hinzufügen**, Vorlage leer lassen:

| Feld | Wert | Hinweis |
| --- | --- | --- |
| Name | `little-sister` | |
| Repository | `ghcr.io/malte007/little_sister:latest` | neueste fertige Version; für eine Testversion z. B. `…:0.1.2-test` |
| Netzwerktyp | `Host` | nötig für die ONVIF-Kamerasuche (Multicast) |
| Erweiterte Ansicht → Zusätzliche Parameter | `--user 99:100` | läuft als Unraid-Standardbenutzer `nobody:users`, sonst fehlen Schreibrechte auf den Shares |
| Pfad `/data` | `/mnt/user/appdata/little-sister` | Datenbank, Schlüssel, Sicherungen |
| Pfad `/recordings` | z. B. `/mnt/user/aufnahmen/little-sister` | Aufnahmen; Share am besten ohne Cache, sonst schiebt der Mover die Dateien ständig hin und her |
| Variable `TZ` | `Europe/Berlin` | |
| WebUI | `http://[IP]:[PORT:8080]/` | |

Mit Host-Netzwerk belegt Little Sister die Ports **8080** (Oberfläche), **8555 TCP/UDP** (WebRTC) und **1984** (go2rtc, nur intern). Läuft auf Unraid schon Frigate oder ein anderes go2rtc, die Ports per Variable umlegen: `ASPNETCORE_HTTP_PORTS`, `LittleSister__Live__WebRtcPort`, `LittleSister__Live__ApiPort`.

Danach `http://<unraid-ip>:8080` öffnen und die Einrichtung durchklicken; als Aufnahmeordner `/recordings` lassen.

**4. Aktualisieren.** Nach dem Push eines neuen Versions-Tags ist das Image nach einigen Minuten fertig (Reiter **Actions** auf GitHub). Dann auf Unraid unter **Docker → Nach Updates suchen** und beim Container **Update anwenden**. Datenbank-Migrationen laufen beim Start automatisch, vorher wird die Datenbank nach `/data/backups` gesichert.

### Selbst bauen mit docker compose

```bash
docker compose up -d --build
```

| Zeile | Wirkung |
| --- | --- |
| `docker compose up -d --build` | baut das Image aus dem Quellcode und startet Little Sister im Hintergrund |

Danach `http://<server>:8080` öffnen; der Einrichtungsassistent startet automatisch. Datenbank und Schlüssel liegen in `./data`, Aufnahmen in `./recordings`; Einstellungen werden in `docker-compose.yml` unter `environment` gesetzt.

Die Compose-Datei nutzt das Host-Netzwerk, weil die ONVIF-Kamerasuche per UDP-Multicast sonst nicht aus dem Container herauskommt. Ohne Suche (Kamera per IP-Adresse oder RTSP-Adresse hinzufügen) reicht auch das normale Bridge-Netzwerk.

## Entwickeln

Voraussetzungen: .NET 10 SDK, Node.js 24 (mindestens 22.12), ffmpeg und go2rtc (für Aufnahme, Live-Ansicht und Tests).

```bash
dotnet run --project src/LittleSister.Api
cd web && npm install && npm run dev
```

| Zeile | Wirkung |
| --- | --- |
| `dotnet run --project src/LittleSister.Api` | startet die API auf Port 5080, auch aus dem LAN erreichbar, damit Kameras Ereignisse schicken können (Daten in `src/LittleSister.Api/data`) |
| `cd web && npm install && npm run dev` | installiert die Frontend-Pakete und startet die Oberfläche auf http://localhost:5173; `/api` wird an die API weitergeleitet |

### Einrichtung in Rider (Linux, fish)

Voraussetzungen: aktuelles Rider, .NET 10 SDK, ffmpeg, go2rtc und Node.js 24. Node am einfachsten über [nvm](https://github.com/nvm-sh/nvm). nvm ist ein bash-Skript, deshalb wird es unter fish über `bash -c` aufgerufen:

```fish
sudo apt install dotnet-sdk-10.0 ffmpeg
bash -c 'source ~/.nvm/nvm.sh && nvm install 24'
fish_add_path ~/.nvm/versions/node/(bash -c 'source ~/.nvm/nvm.sh && nvm version 24')/bin
node --version; npm --version
```

| Zeile | Wirkung |
| --- | --- |
| `sudo apt install …` | installiert .NET 10 SDK und ffmpeg (für Aufnahme und Tests) |
| `bash -c '… nvm install 24'` | lädt nvm in einer bash und installiert Node.js 24 |
| `fish_add_path …` | trägt den Node-24-Ordner dauerhaft in den fish-Suchpfad ein, damit `node` und `npm` überall gefunden werden |
| `node --version; npm --version` | prüft, ob Node 24 und npm 11 aktiv sind |

go2rtc für die Live-Ansicht (eine einzelne Programmdatei, nach `/usr/local/bin`, damit auch Rider sie findet):

```fish
curl -L -o /tmp/go2rtc https://github.com/AlexxIT/go2rtc/releases/download/v1.9.14/go2rtc_linux_amd64
sudo install -m 755 /tmp/go2rtc /usr/local/bin/go2rtc
go2rtc -version
```

| Zeile | Wirkung |
| --- | --- |
| `curl -L -o /tmp/go2rtc …` | lädt go2rtc 1.9.14 für 64-Bit-Linux herunter (auf ARM `go2rtc_linux_arm64`) |
| `sudo install -m 755 …` | kopiert es ausführbar nach `/usr/local/bin` |
| `go2rtc -version` | prüft, ob es gefunden wird |

Projekt holen und öffnen:

```fish
git clone https://github.com/malte007/little_sister.git
cd little_sister
git switch main
```

| Zeile | Wirkung |
| --- | --- |
| `git clone …` | lädt das Repository herunter |
| `cd little_sister` | wechselt in den Projektordner |
| `git switch main` | wechselt auf den gewünschten Branch (für einen offenen PR dessen Branch angeben) |

In Rider:

1. **File → Open** und `LittleSister.slnx` öffnen.
2. Oben rechts die Startkonfiguration **LittleSister.Api: http** wählen und starten. Die API läuft auf http://localhost:5080, Datenbank und Schlüssel liegen in `src/LittleSister.Api/data`. go2rtc startet automatisch mit (Ports 1984 lokal und 8555).
3. Tests: Fenster **Unit Tests** öffnen und **Run All** ausführen (Kamera- und Live-Tests brauchen ffmpeg und go2rtc).
4. Oberfläche im Rider-Terminal (fish) starten:

```fish
cd web
npm install
npm run dev
```

| Zeile | Wirkung |
| --- | --- |
| `cd web` | wechselt in den Frontend-Ordner |
| `npm install` | installiert die Frontend-Pakete; beim ersten Mal und nach jedem `git pull`, der `package.json` ändert, sonst meldet Vite „Failed to resolve import“ |
| `npm run dev` | startet die Oberfläche auf http://localhost:5173 und leitet `/api` an die laufende API weiter |

5. http://localhost:5173 im Browser öffnen und die Einrichtung durchlaufen. Als Aufnahmeordner einen beschreibbaren absoluten Pfad angeben, z. B. `/home/<du>/little-sister/recordings`. Admins können ihn später unter **Kameras → Speicher** ändern.

Von vorn beginnen: API stoppen und den Ordner `src/LittleSister.Api/data` löschen.

### Bauen und Testen wie die CI

Alles bauen und testen, genau wie die CI:

```bash
./scripts/build.sh
./scripts/docker-build.sh
```

| Zeile | Wirkung |
| --- | --- |
| `./scripts/build.sh` | prüft und baut die Oberfläche, baut die Solution und führt alle Tests aus |
| `./scripts/docker-build.sh` | baut das Docker-Image `little-sister:dev` (mit `PUSH=true` und `IMAGE=…` auch veröffentlichen) |

Neue Datenbank-Migration anlegen:

```bash
dotnet ef migrations add <Name> -p src/LittleSister.Infrastructure -s src/LittleSister.Infrastructure -o Data/Migrations
```

| Zeile | Wirkung |
| --- | --- |
| `dotnet ef migrations add …` | erzeugt eine Migration; sie wird beim nächsten Start automatisch angewendet (vorher wird die Datenbank nach `data/backups` gesichert) |

## CI (GitHub und Gitea)

`.github/workflows/ci.yml` läuft auf GitHub Actions und Gitea Actions (`act_runner`). Die eigentliche Logik steckt in `scripts/`, der Workflow ruft sie nur auf. Bei jedem Commit auf `main` und bei Pull Requests nach `main` wird gebaut und getestet. Nur wenn ein Versions-Tag `v*` gepusht wird, entsteht zusätzlich das Docker-Image und wird veröffentlicht (siehe „Fertiges Image von GitHub“). Von Hand startest du die Tests unter GitHub → Actions → CI → „Run workflow“.

Für Gitea unter *Repository → Einstellungen → Actions*:

| Name | Art | Beispiel |
| --- | --- | --- |
| `REGISTRY` | Variable | `gitea.local:3000` |
| `IMAGE_NAME` | Variable | `malte/little-sister` |
| `RUNNER_LABEL` | Variable (optional) | `ubuntu-latest` |
| `REGISTRY_USERNAME` / `REGISTRY_PASSWORD` | Secrets | Gitea-Benutzer und Token mit Paket-Schreibrecht |

## Lizenz

Little Sister ist **proprietär**, alle Rechte vorbehalten (siehe [LICENSE](LICENSE)). Nutzung, Weitergabe und Änderung nur mit Erlaubnis des Urhebers.

- Mitgelieferte Fremdsoftware und ihre Lizenzen stehen in [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES); die Datei liegt auch im Docker-Image unter `/app/THIRD-PARTY-NOTICES`.
- Neue Abhängigkeiten nur mit freizügiger Lizenz (MIT, BSD, Apache 2.0, PostgreSQL-Lizenz). Keine GPL-Bibliotheken im eigenen Prozess; ffmpeg wird nur als eigener Prozess gestartet.
- Beiträge Dritter nur mit Contributor License Agreement.
