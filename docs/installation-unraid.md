# Little Sister auf Unraid (Image aus der Gitea-CI)

Ablauf: GitHub bleibt die Quelle. Gitea spiegelt das Repository alle 10 Minuten, die Gitea-CI baut bei jeder Änderung auf `main` das Docker-Image und legt es in der Gitea-Registry ab. Unraid holt das Image von dort.

```
GitHub (main) ──Spiegel──▶ Gitea ──CI──▶ Gitea-Registry ──▶ Unraid-Container
```

## 1. Repository in Gitea spiegeln

1. In Gitea oben rechts **+ → Neue Migration → GitHub**.
2. URL: `https://github.com/Malte007/Little_Sister.git`. Weil das Repository privat ist, ein GitHub-Token mit Lesezugriff eintragen (GitHub → Settings → Developer settings → Fine-grained token, nur dieses Repository, „Contents: Read“).
3. **„Dieses Repository wird ein Mirror sein“** anhaken, Intervall `10m`.
4. Nach dem Anlegen: **Einstellungen → Erweitert → Actions** aktivieren, falls der Haken fehlt.

Ein Abgleich lässt sich jederzeit unter **Einstellungen → Spiegel-Einstellungen → Jetzt synchronisieren** anstoßen.

> Gitea startet die Actions bei einem Spiegel-Abgleich wie bei einem Push ([Issue #32412](https://github.com/go-gitea/gitea/issues/32412)). Startet nach einem Abgleich kein Lauf, den Workflow im Reiter **Actions** von Hand starten (`workflow_dispatch` ist eingerichtet).

## 2. CI-Variablen und Zugangsdaten

Im gespiegelten Repository unter **Einstellungen → Actions**:

| Art | Name | Wert |
| --- | --- | --- |
| Variable | `REGISTRY` | Gitea-Adresse ohne `http://`, z. B. `192.168.0.10:3000` |
| Variable | `IMAGE_NAME` | z. B. `malte/little_sister` (Gitea-Benutzer/Repository, klein geschrieben) |
| Variable | `RUNNER_LABEL` | nur nötig, wenn der Runner kein Label `ubuntu-latest` hat |
| Secret | `REGISTRY_USERNAME` | dein Gitea-Benutzername |
| Secret | `REGISTRY_PASSWORD` | Gitea-Token mit Berechtigung `write:package` (Einstellungen → Anwendungen → Token generieren) |

Die Labels des Runners stehen unter **Website-Administration → Actions → Runner**.

### Runner in Docker

Der Job baut das Image mit `docker build`. Dafür braucht der Job-Container Zugriff auf Docker. act_runner reicht den Docker-Socket standardmäßig an die Job-Container weiter (`container.docker_host: ""` in der `config.yaml`). Steht dort `"-"`, auf `""` ändern und den Runner neu starten.

### Registry ohne HTTPS

Ist Gitea nur per `http://` erreichbar, verweigert Docker das Hoch- und Herunterladen. Dann muss der Docker-Dienst auf Unraid die Adresse als unsichere Registry kennen (`"insecure-registries": ["192.168.0.10:3000"]` in `/etc/docker/daemon.json`). Auf Unraid geht diese Datei beim Neustart verloren und muss z. B. über `/boot/config/go` neu geschrieben werden. Läuft Gitea hinter einem Reverse-Proxy mit HTTPS, entfällt das.

## 3. Container auf Unraid anlegen

**Docker → Container hinzufügen**, Vorlage „leer“:

| Feld | Wert | Hinweis |
| --- | --- | --- |
| Name | `little-sister` | |
| Repository | `192.168.0.10:3000/malte/little_sister:latest` | Registry und Image-Name wie oben |
| Netzwerktyp | `Host` | nötig für die ONVIF-Kamerasuche (Multicast) |
| Erweiterte Ansicht → Zusätzliche Parameter | `--user 99:100` | schreibt als Unraid-Standardbenutzer `nobody:users`, sonst fehlen Schreibrechte auf den Shares |
| Pfad `/data` | `/mnt/user/appdata/little-sister` | Datenbank, Schlüssel, Sicherungen |
| Pfad `/recordings` | z. B. `/mnt/user/aufnahmen/little-sister` | Aufnahmen |
| Variable `TZ` | `Europe/Berlin` | |
| Variable `ASPNETCORE_HTTP_PORTS` | `8080` | anderen Port wählen, falls 8080 belegt ist |
| WebUI | `http://[IP]:[PORT:8080]/` | |

Mit Host-Netzwerk belegt Little Sister zusätzlich **1984** (go2rtc, nur intern auf 127.0.0.1) und **8555 TCP/UDP** (WebRTC). Läuft auf Unraid schon ein anderes go2rtc oder Frigate, die Ports per Variable umlegen: `LittleSister__Live__ApiPort` und `LittleSister__Live__WebRtcPort`.

Für den Aufnahme-Share eignet sich ein Share direkt auf dem Array oder einem eigenen Pool. Mit Cache „Ja“ schiebt der Mover die Dateien ständig hin und her, während der Ringspeicher sie wieder löscht.

Danach `http://<unraid-ip>:8080` öffnen und die Einrichtung durchklicken. Als Aufnahmeordner `/recordings` lassen.

## 4. Aktualisieren

Nach einem Merge auf GitHub dauert es bis zu 10 Minuten (Spiegel) plus ein paar Minuten (CI). Dann auf Unraid unter **Docker** beim Container **Update** bzw. „Nach Updates suchen“ wählen. Datenbank-Migrationen laufen beim Start automatisch, vorher wird die Datenbank nach `/data/backups` gesichert.

## 5. Testliste

- [ ] Einrichtung, Anmeldung, zweiter Benutzer als Betrachter
- [ ] Kameras per ONVIF-Suche finden und hinzufügen, Stream-Test für Haupt- und Substream
- [ ] Aufnahme: nach 1–2 Minuten liegen Segmente unter `/recordings/<kamera-id>/<datum>/`
- [ ] Live-Kacheln und Einzelansicht, Vollbild, Drehung
- [ ] Kamera kurz vom Netz trennen: Status wird „Gestört“, danach kommen Aufnahme und Live-Bild von allein zurück
- [ ] Container neu starten: Einstellungen und Kameras bleiben erhalten
