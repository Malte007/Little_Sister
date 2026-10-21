# Little Sister

Selbst gehosteter Dienst für Überwachungskameras: Kameras verwalten, Aufnahmen in einem Ringspeicher ablegen und über eine Zeitleiste mit Ereignismarkern abspielen.

- Backend: ASP.NET Core (.NET 10), EF Core mit SQLite
- Frontend: SvelteKit + TypeScript (als statische Single-Page-App vom Backend ausgeliefert)
- Live-Ansicht: go2rtc (folgt)
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
| Live-Ansicht, Zeitleiste, Aufnahme bei Bewegung | folgt |

## Starten mit Docker

```bash
docker compose up -d --build
```

| Zeile | Wirkung |
| --- | --- |
| `docker compose up -d --build` | baut das Image aus dem Quellcode und startet Little Sister im Hintergrund |

Danach `http://<server>:8080` öffnen; der Einrichtungsassistent startet automatisch. Datenbank und Schlüssel liegen in `./data`, Aufnahmen in `./recordings`.

Die Compose-Datei nutzt das Host-Netzwerk, weil die ONVIF-Kamerasuche per UDP-Multicast sonst nicht aus dem Container herauskommt. Ohne Suche (Kamera per IP-Adresse oder RTSP-Adresse hinzufügen) reicht auch das normale Bridge-Netzwerk.

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
| `ASPNETCORE_HTTP_PORTS` | `8080` | Port im Container |

## Entwickeln

Voraussetzungen: .NET 10 SDK, Node.js 24 (mindestens 22.12), ffmpeg (für Aufnahme und Tests).

```bash
dotnet run --project src/LittleSister.Api
cd web && npm install && npm run dev
```

| Zeile | Wirkung |
| --- | --- |
| `dotnet run --project src/LittleSister.Api` | startet die API auf http://localhost:5080 (Daten in `src/LittleSister.Api/data`) |
| `cd web && npm install && npm run dev` | installiert die Frontend-Pakete und startet die Oberfläche auf http://localhost:5173; `/api` wird an die API weitergeleitet |

### Einrichtung in Rider (Linux, fish)

Voraussetzungen: aktuelles Rider, .NET 10 SDK, ffmpeg und Node.js 24. Node am einfachsten über [nvm](https://github.com/nvm-sh/nvm). nvm ist ein bash-Skript, deshalb wird es unter fish über `bash -c` aufgerufen:

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
2. Oben rechts die Startkonfiguration **LittleSister.Api: http** wählen und starten. Die API läuft auf http://localhost:5080, Datenbank und Schlüssel liegen in `src/LittleSister.Api/data`.
3. Tests: Fenster **Unit Tests** öffnen und **Run All** ausführen (Kamera-Tests brauchen ffmpeg).
4. Oberfläche im Rider-Terminal (fish) starten:

```fish
cd web
npm install
npm run dev
```

| Zeile | Wirkung |
| --- | --- |
| `cd web` | wechselt in den Frontend-Ordner |
| `npm install` | installiert die Frontend-Pakete (nur beim ersten Mal oder nach Änderungen an `package.json`) |
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

[AGPL-3.0](LICENSE)
