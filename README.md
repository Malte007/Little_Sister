# Little Sister

Selbst gehosteter Dienst für Überwachungskameras: Kameras verwalten, Aufnahmen in einem Ringspeicher ablegen und über eine Zeitleiste mit Ereignismarkern abspielen.

- Backend: ASP.NET Core (.NET 10), EF Core mit SQLite
- Frontend: SvelteKit + TypeScript (als statische Single-Page-App vom Backend ausgeliefert)
- Live-Ansicht: go2rtc (WebRTC, MSE als Rückfall), von Little Sister selbst gestartet
- Betrieb: Linux, Docker (z. B. Unraid)

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
| Vorschaubilder auf der Zeitleiste, mehrere Kameras gleichzeitig abspielen | folgt |
| Aufnahme bei Bewegung (ONVIF-Ereignisse, Marker auf der Zeitleiste) | folgt |

## Starten mit Docker

```bash
docker compose up -d --build
```

| Zeile | Wirkung |
| --- | --- |
| `docker compose up -d --build` | baut das Image aus dem Quellcode und startet Little Sister im Hintergrund |

Danach `http://<server>:8080` öffnen; der Einrichtungsassistent startet automatisch. Datenbank und Schlüssel liegen in `./data`, Aufnahmen in `./recordings`.

Die Compose-Datei nutzt das Host-Netzwerk, weil die ONVIF-Kamerasuche per UDP-Multicast sonst nicht aus dem Container herauskommt. Ohne Suche (Kamera per IP-Adresse oder RTSP-Adresse hinzufügen) reicht auch das normale Bridge-Netzwerk.

### Live-Ansicht

Little Sister startet go2rtc selbst (im Docker-Image enthalten) und hält dessen Streams mit den Kameras in Sync. Die go2rtc-API lauscht nur auf `127.0.0.1`; der Browser bekommt Bild und Standbilder ausschließlich über die angemeldete Little-Sister-API. Die Kacheln nutzen den Substream, die Einzelansicht wahlweise den Hauptstream.

Für WebRTC (Latenz unter einer Sekunde) muss Port **8555 (TCP und UDP)** vom Browser aus erreichbar sein. Ist er es nicht, fällt der Player automatisch auf MSE über die normale Verbindung zurück (etwas mehr Verzögerung). Fehlt go2rtc, ist nur die Live-Ansicht aus, die Aufnahme läuft weiter.

### Wiedergabe und Export

Unter **Aufnahmen** zeigt die Zeitleiste pro Kamera und Tag, wann aufgenommen wurde. Ein Klick springt an die Stelle, Lücken werden beim Abspielen übersprungen. Abgespielt wird per HLS direkt aus den gespeicherten Segmenten, ohne Umwandlung und ohne CPU-Last auf dem Server. Der Export setzt den markierten Zeitraum ebenfalls ohne Umwandlung zu einer MP4-Datei zusammen; er beginnt am Schlüsselbild vor dem gewählten Anfang. Tagesgrenzen und Uhrzeiten richten sich nach der Zeitzone der Installation.

### Ablage der Aufnahmen

```
recordings/<kamera-id>/<JJJJMMTT>/<HHMMSS>.mp4   fertige Segmente (Zeiten in UTC)
recordings/<kamera-id>/.incoming/                 Segment, das gerade geschrieben wird
```

Segmente sind fragmentierte MP4-Dateien und lassen sich direkt mit jedem Player öffnen.

### Konfiguration

Alle Einstellungen lassen sich per Umgebungsvariable setzen (doppelter Unterstrich trennt Ebenen):

| Variable | Standard | Bedeutung |
| --- | --- | --- |
| `LittleSister__DataPath` | `/data` (Docker) | Datenbank, Schlüssel, Sicherungen |
| `LittleSister__DefaultRecordingsPath` | `/recordings` (Docker) | Vorschlag im Einrichtungsassistenten |
| `LittleSister__Database__Provider` | `Sqlite` | weitere Provider folgen |
| `LittleSister__Database__ConnectionString` | SQLite-Datei im Datenordner | eigene Verbindung |
| `LittleSister__KeysPath` | `<DataPath>/keys` | Schlüssel für Cookies und Kamera-Passwörter, getrennt von der DB ablegbar |
| `LittleSister__Recording__SegmentSeconds` | `60` | Länge eines Aufnahmesegments |
| `LittleSister__Recording__FfmpegPath` | `ffmpeg` | Pfad zu ffmpeg (im Image enthalten) |
| `LittleSister__Live__Go2rtcPath` | `go2rtc` | Pfad zu go2rtc (im Image enthalten) |
| `LittleSister__Live__ApiPort` | `1984` | go2rtc-API, nur auf 127.0.0.1 |
| `LittleSister__Live__WebRtcPort` | `8555` | WebRTC-Port (TCP und UDP) |
| `LittleSister__Live__WebRtcCandidates__0` | leer | zusätzliche WebRTC-Adresse, z. B. `192.168.1.10:8555` bei Docker-Bridge oder NAT |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Port im Container |

## Entwickeln

Voraussetzungen: .NET 10 SDK, Node.js 24 (mindestens 22.12), ffmpeg und go2rtc (für Aufnahme, Live-Ansicht und Tests).

```bash
dotnet run --project src/LittleSister.Api
cd web && npm install && npm run dev
```

| Zeile | Wirkung |
| --- | --- |
| `dotnet run --project src/LittleSister.Api` | startet die API auf http://localhost:5080 (Daten in `src/LittleSister.Api/data`) |
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

`.github/workflows/ci.yml` läuft auf GitHub Actions und Gitea Actions (`act_runner`). Die eigentliche Logik steckt in `scripts/`, der Workflow ruft sie nur auf. Bei Pull Requests wird gebaut und getestet, auf `main` und bei Tags `v*` zusätzlich das Image veröffentlicht.

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
