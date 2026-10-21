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
