#!/usr/bin/env bash
# Installiert oder aktualisiert Little Sister auf einem Ubuntu-Server als systemd-Dienst (ohne Docker).
# Aufruf als normaler Benutzer; für Systemschritte wird sudo verwendet. Zwei Wege:
#   ./scripts/install-ubuntu.sh                  aus dem Projektordner: baut aus dem Quellcode (braucht .NET SDK und Node.js)
#   ./install-ubuntu.sh --release [v0.2.0]       lädt das fertige Paket eines GitHub-Release (Standard: neuestes);
#                                                braucht nur die ASP.NET-Core-Laufzeit 10 und ffmpeg
# Fehlt etwas davon, bietet das Skript an, es zu installieren.
#
# Anpassbar per Umgebungsvariable:
#   INSTALL_DIR     Programmdateien              (Standard: /opt/little-sister)
#   DATA_DIR        Datenbank, Schlüssel         (Standard: /var/lib/little-sister)
#   RECORDINGS_DIR  Vorschlag für Aufnahmen      (Standard: /srv/little-sister/recordings)
#   GO2RTC_VERSION  go2rtc für die Live-Ansicht  (Standard: 1.9.14)
#   HTTP_PORT       Port für Oberfläche und API  (nur Erstinstallation; sonst wird gefragt, Standard 8080)
#   GITHUB_REPO     Repository der Releases      (Standard: Malte007/Little_Sister)
#   GITHUB_TOKEN    nur bei privatem Repository: Token mit Lesezugriff auf Contents
set -euo pipefail

# Aufrufart: aus dem Quellcode bauen oder ein Release laden
RELEASE=""
if [[ "${1:-}" == "--release" ]]; then
  RELEASE="${2:-latest}"
elif [[ -n "${1:-}" ]]; then
  echo "Aufruf: $0 [--release [version]]" >&2
  exit 1
fi
[[ -z "$RELEASE" ]] && cd "$(dirname "$0")/.."

INSTALL_DIR="${INSTALL_DIR:-/opt/little-sister}"
DATA_DIR="${DATA_DIR:-/var/lib/little-sister}"
RECORDINGS_DIR="${RECORDINGS_DIR:-/srv/little-sister/recordings}"
GO2RTC_VERSION="${GO2RTC_VERSION:-1.9.14}"
GITHUB_REPO="${GITHUB_REPO:-Malte007/Little_Sister}"
SERVICE_USER=littlesister
ENV_FILE=/etc/little-sister/little-sister.env

fail() { echo "Fehler: $*" >&2; exit 1; }

# Ja/Nein-Frage über das Terminal; Enter bedeutet Ja. Ohne Terminal (z. B. per Pipe) gilt Nein.
ask() {
  local answer
  ( : </dev/tty ) 2>/dev/null || return 1
  read -rp "$1 [J/n] " answer </dev/tty || return 1
  [[ -z "$answer" || "$answer" =~ ^[JjYy] ]]
}

# Lauscht schon ein Programm auf diesem TCP-Port? (ss aus iproute2, sonst Verbindungsversuch auf localhost)
port_in_use() {
  if command -v ss >/dev/null; then
    ss -Htln "sport = :$1" | grep -q .
  else
    (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null
  fi
}

# Port für Oberfläche und API: aus HTTP_PORT oder per Frage, Enter = 8080. Ohne Terminal gilt der Standard.
choose_port() {
  local port="${HTTP_PORT:-}"
  while true; do
    if [[ -z "$port" ]]; then
      if ( : </dev/tty ) 2>/dev/null; then
        read -rp "Port für Little Sister (Oberfläche und API) [8080]: " port </dev/tty || port=""
      fi
      port="${port:-8080}"
    fi
    if [[ ! "$port" =~ ^[0-9]+$ ]] || (( port < 1 || port > 65535 )); then
      [[ -n "${HTTP_PORT:-}" ]] && fail "HTTP_PORT=$HTTP_PORT ist kein gültiger Port (1–65535)"
      echo "Ungültiger Port: $port (erlaubt 1–65535)" >&2
    elif port_in_use "$port"; then
      echo "Port $port ist bereits belegt:" >&2
      command -v ss >/dev/null && { sudo ss -Htlnp "sport = :$port" >&2 || true; }
      [[ -n "${HTTP_PORT:-}" ]] && fail "Bitte einen freien Port wählen"
      ( : </dev/tty ) 2>/dev/null || fail "Port $port ist belegt; mit HTTP_PORT=<port> einen anderen wählen"
    else
      CHOSEN_PORT="$port"
      return
    fi
    port=""
  done
}

has_dotnet10() { command -v dotnet >/dev/null && dotnet --list-sdks | grep -q '^10\.'; }
has_aspnet10() { command -v dotnet >/dev/null && dotnet --list-runtimes | grep -q '^Microsoft\.AspNetCore\.App 10\.'; }
# Was für den gewählten Weg an .NET gebraucht wird: SDK zum Bauen, sonst nur die Laufzeit
if [[ -n "$RELEASE" ]]; then
  DOTNET_PACKAGE=aspnetcore-runtime-10.0
  dotnet_ok() { has_aspnet10; }
else
  DOTNET_PACKAGE=dotnet-sdk-10.0
  dotnet_ok() { has_dotnet10; }
fi

# Lädt little-sister.tar.gz eines Release nach $1 und prüft die Prüfsumme.
# Ohne Token über die öffentlichen Download-Adressen, mit Token über die GitHub-API (privates Repository).
download_release() {
  local target="$1" file
  if [[ -z "${GITHUB_TOKEN:-}" ]]; then
    local base="https://github.com/$GITHUB_REPO/releases"
    [[ "$RELEASE" == latest ]] && base+="/latest/download" || base+="/download/$RELEASE"
    for file in little-sister.tar.gz little-sister.tar.gz.sha256; do
      curl -fL --progress-bar -o "$target/$file" "$base/$file" \
        || fail "Download von $base/$file fehlgeschlagen (Repository privat? Dann GITHUB_TOKEN setzen)"
    done
  else
    local api="https://api.github.com/repos/$GITHUB_REPO/releases" release
    command -v python3 >/dev/null || fail "python3 fehlt (sudo apt install python3)"
    [[ "$RELEASE" == latest ]] && api+="/latest" || api+="/tags/$RELEASE"
    release="$(curl -fsSL -H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/vnd.github+json" "$api")" \
      || fail "Release $RELEASE nicht gefunden (Token gültig und mit Lesezugriff auf Contents?)"
    for file in little-sister.tar.gz little-sister.tar.gz.sha256; do
      local url
      url="$(python3 -c 'import json,sys; r=json.load(sys.stdin); print(next(a["url"] for a in r["assets"] if a["name"]==sys.argv[1]))' "$file" <<<"$release")" \
        || fail "Im Release fehlt $file"
      curl -fL --progress-bar -H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/octet-stream" -o "$target/$file" "$url" \
        || fail "Download von $file fehlgeschlagen"
    done
  fi
  (cd "$target" && sha256sum -c --quiet little-sister.tar.gz.sha256) || fail "Prüfsumme stimmt nicht, Download beschädigt"
}
node_ok() {
  command -v node >/dev/null &&
    node -e 'const [a,b]=process.versions.node.split(".").map(Number); process.exit(a>22||(a===22&&b>=12)?0:1)'
}

# 1) Voraussetzungen prüfen und fehlende nach Rückfrage installieren
MISSING=()
command -v curl >/dev/null || MISSING+=(curl)
command -v ffmpeg >/dev/null || MISSING+=(ffmpeg)
dotnet_ok || MISSING+=("$DOTNET_PACKAGE")
[[ -z "$RELEASE" ]] && ! node_ok && MISSING+=("nodejs (Version 24 über die Paketquelle von NodeSource)")

if (( ${#MISSING[@]} > 0 )); then
  echo "Es fehlen:"
  printf '  - %s\n' "${MISSING[@]}"
  ask "Jetzt installieren?" || fail "Bitte die fehlenden Programme installieren (siehe README) und das Skript erneut starten"

  sudo apt-get update
  APT_PACKAGES=()
  command -v curl >/dev/null || APT_PACKAGES+=(curl)
  command -v ffmpeg >/dev/null || APT_PACKAGES+=(ffmpeg)
  if ! dotnet_ok; then
    # Ältere Ubuntu-Versionen führen .NET 10 nur im Backports-PPA
    if ! apt-cache show "$DOTNET_PACKAGE" >/dev/null 2>&1; then
      ask ".NET 10 ist in den Ubuntu-Paketquellen nicht enthalten. Paketquelle ppa:dotnet/backports hinzufügen?" \
        || fail ".NET 10 fehlt"
      sudo apt-get install -y software-properties-common
      sudo add-apt-repository -y ppa:dotnet/backports
    fi
    APT_PACKAGES+=("$DOTNET_PACKAGE")
  fi
  (( ${#APT_PACKAGES[@]} > 0 )) && sudo apt-get install -y "${APT_PACKAGES[@]}"

  if [[ -z "$RELEASE" ]] && ! node_ok; then
    curl -fsSL https://deb.nodesource.com/setup_24.x | sudo -E bash -
    sudo apt-get install -y nodejs
  fi
fi

# Zur Sicherheit erneut prüfen (z. B. ein älteres Node über nvm im PATH vor /usr/bin)
dotnet_ok || fail ".NET 10 fehlt (sudo apt install $DOTNET_PACKAGE)"
command -v ffmpeg >/dev/null || fail "ffmpeg fehlt (sudo apt install ffmpeg)"
[[ -n "$RELEASE" ]] || node_ok || fail "Node.js fehlt oder ist zu alt ($(node --version 2>/dev/null || echo 'nicht gefunden')), benötigt wird mindestens 22.12"

# Port nur bei der Erstinstallation festlegen; bei Updates bleibt der eingestellte (siehe ENV_FILE)
CHOSEN_PORT=""
if [[ ! -f "$ENV_FILE" ]]; then
  choose_port
  echo "Little Sister wird auf Port $CHOSEN_PORT eingerichtet"
fi

# 2) go2rtc installieren bzw. auf die gewünschte Version bringen
if ! go2rtc -version 2>/dev/null | grep -q "$GO2RTC_VERSION"; then
  case "$(uname -m)" in aarch64|arm64) ARCH=arm64 ;; *) ARCH=amd64 ;; esac
  echo "go2rtc $GO2RTC_VERSION wird installiert"
  curl -fsSL -o /tmp/go2rtc "https://github.com/AlexxIT/go2rtc/releases/download/v${GO2RTC_VERSION}/go2rtc_linux_${ARCH}"
  sudo install -m 755 /tmp/go2rtc /usr/local/bin/go2rtc
  rm -f /tmp/go2rtc
fi

# 3) Paket holen: fertiges Release laden oder aus dem Quellcode bauen
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT
if [[ -n "$RELEASE" ]]; then
  echo "Release $RELEASE wird geladen"
  download_release "$WORK_DIR"
else
  OUT="$WORK_DIR" ./scripts/package.sh
fi
tar -xzf "$WORK_DIR/little-sister.tar.gz" -C "$WORK_DIR"
PUBLISH_DIR="$WORK_DIR/little-sister"
OLD_VERSION="$(cat "$INSTALL_DIR/app/VERSION" 2>/dev/null || echo "keine")"
echo "Installiert: $OLD_VERSION, neu: $(cat "$PUBLISH_DIR/VERSION")"

# 4) Dienstbenutzer und Ordner anlegen (Daten bleiben bei Updates erhalten)
id "$SERVICE_USER" >/dev/null 2>&1 || sudo useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
sudo mkdir -p "$INSTALL_DIR" "$DATA_DIR" "$RECORDINGS_DIR" "$(dirname "$ENV_FILE")"
sudo chown "$SERVICE_USER:$SERVICE_USER" "$DATA_DIR" "$RECORDINGS_DIR"

# 5) Programmdateien austauschen
sudo systemctl stop little-sister 2>/dev/null || true
sudo rm -rf "$INSTALL_DIR/app"
sudo cp -r "$PUBLISH_DIR" "$INSTALL_DIR/app"
sudo chmod -R a+rX "$INSTALL_DIR/app"

# 6) Einstellungen nur beim ersten Mal anlegen, damit eigene Änderungen erhalten bleiben
if [[ ! -f "$ENV_FILE" ]]; then
  sudo tee "$ENV_FILE" >/dev/null <<EOF
# Einstellungen für Little Sister; nach Änderungen: sudo systemctl restart little-sister
ASPNETCORE_HTTP_PORTS=$CHOSEN_PORT
LittleSister__DataPath=$DATA_DIR
LittleSister__DefaultRecordingsPath=$RECORDINGS_DIR
EOF
fi

# 7) systemd-Dienst: startet mit dem Server und nach einem Absturz neu.
# Liegt der Aufnahmeordner auf einer eigenen Einhängung (z. B. Freigabe mit "nofail" in /etc/fstab), wartet der Dienst
# beim Start auf sie und wird beim Herunterfahren vor dem Aushängen gestoppt. Nur Reihenfolge: Fehlt sie, startet er trotzdem.
MOUNT_AFTER=""
MOUNT_POINT="$(findmnt -n -o TARGET --target "$RECORDINGS_DIR" 2>/dev/null || true)"
if [[ -n "$MOUNT_POINT" && "$MOUNT_POINT" != "/" ]]; then
  MOUNT_AFTER=" $(systemd-escape -p --suffix=mount "$MOUNT_POINT")"
  echo "Dienst wartet auf die Einhängung $MOUNT_POINT"
fi
sudo tee /etc/systemd/system/little-sister.service >/dev/null <<EOF
[Unit]
Description=Little Sister (Kameras, Aufnahme, Live-Ansicht)
# Erst nach dem Einhängen von Platten und Freigaben starten (fehlt eine, startet der Dienst trotzdem)
After=network-online.target local-fs.target remote-fs.target$MOUNT_AFTER
Wants=network-online.target

[Service]
User=$SERVICE_USER
WorkingDirectory=$INSTALL_DIR/app
EnvironmentFile=$ENV_FILE
ExecStart=/usr/bin/dotnet $INSTALL_DIR/app/LittleSister.Api.dll
Restart=always
RestartSec=5
# ffmpeg mit "q" beenden lassen, damit das letzte Segment vollständig geschrieben wird
TimeoutStopSec=30

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now little-sister
sudo systemctl restart little-sister

PORT="$(sudo grep -oP '^ASPNETCORE_HTTP_PORTS=\K\d+' "$ENV_FILE" || echo 8080)"
echo
echo "Little Sister läuft: http://$(hostname -I | awk '{print $1}'):$PORT"
echo "Protokoll ansehen:   journalctl -u little-sister -f"
