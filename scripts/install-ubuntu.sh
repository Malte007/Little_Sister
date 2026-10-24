#!/usr/bin/env bash
# Installiert oder aktualisiert Little Sister auf einem Ubuntu-Server als systemd-Dienst (ohne Docker).
# Aufruf als normaler Benutzer aus dem Projektordner; für Systemschritte wird sudo verwendet.
# Fehlen .NET 10, ffmpeg oder Node.js, bietet das Skript an, sie zu installieren.
#
# Anpassbar per Umgebungsvariable:
#   INSTALL_DIR     Programmdateien              (Standard: /opt/little-sister)
#   DATA_DIR        Datenbank, Schlüssel         (Standard: /var/lib/little-sister)
#   RECORDINGS_DIR  Vorschlag für Aufnahmen      (Standard: /srv/little-sister/recordings)
#   GO2RTC_VERSION  go2rtc für die Live-Ansicht  (Standard: 1.9.14)
#   HTTP_PORT       Port für Oberfläche und API  (nur Erstinstallation; sonst wird gefragt, Standard 8080)
set -euo pipefail
cd "$(dirname "$0")/.."

INSTALL_DIR="${INSTALL_DIR:-/opt/little-sister}"
DATA_DIR="${DATA_DIR:-/var/lib/little-sister}"
RECORDINGS_DIR="${RECORDINGS_DIR:-/srv/little-sister/recordings}"
GO2RTC_VERSION="${GO2RTC_VERSION:-1.9.14}"
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
node_ok() {
  command -v node >/dev/null &&
    node -e 'const [a,b]=process.versions.node.split(".").map(Number); process.exit(a>22||(a===22&&b>=12)?0:1)'
}

# 1) Voraussetzungen prüfen und fehlende nach Rückfrage installieren
MISSING=()
command -v curl >/dev/null || MISSING+=(curl)
command -v ffmpeg >/dev/null || MISSING+=(ffmpeg)
has_dotnet10 || MISSING+=(dotnet-sdk-10.0)
node_ok || MISSING+=("nodejs (Version 24 über die Paketquelle von NodeSource)")

if (( ${#MISSING[@]} > 0 )); then
  echo "Es fehlen:"
  printf '  - %s\n' "${MISSING[@]}"
  ask "Jetzt installieren?" || fail "Bitte die fehlenden Programme installieren (siehe README) und das Skript erneut starten"

  sudo apt-get update
  APT_PACKAGES=()
  command -v curl >/dev/null || APT_PACKAGES+=(curl)
  command -v ffmpeg >/dev/null || APT_PACKAGES+=(ffmpeg)
  if ! has_dotnet10; then
    # Ältere Ubuntu-Versionen führen .NET 10 nur im Backports-PPA
    if ! apt-cache show dotnet-sdk-10.0 >/dev/null 2>&1; then
      ask ".NET 10 ist in den Ubuntu-Paketquellen nicht enthalten. Paketquelle ppa:dotnet/backports hinzufügen?" \
        || fail ".NET 10 SDK fehlt"
      sudo apt-get install -y software-properties-common
      sudo add-apt-repository -y ppa:dotnet/backports
    fi
    APT_PACKAGES+=(dotnet-sdk-10.0)
  fi
  (( ${#APT_PACKAGES[@]} > 0 )) && sudo apt-get install -y "${APT_PACKAGES[@]}"

  if ! node_ok; then
    curl -fsSL https://deb.nodesource.com/setup_24.x | sudo -E bash -
    sudo apt-get install -y nodejs
  fi
fi

# Zur Sicherheit erneut prüfen (z. B. ein älteres Node über nvm im PATH vor /usr/bin)
has_dotnet10 || fail ".NET 10 SDK fehlt (sudo apt install dotnet-sdk-10.0)"
command -v ffmpeg >/dev/null || fail "ffmpeg fehlt (sudo apt install ffmpeg)"
node_ok || fail "Node.js fehlt oder ist zu alt ($(node --version 2>/dev/null || echo 'nicht gefunden')), benötigt wird mindestens 22.12"

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

# 3) Weboberfläche und Backend bauen
echo "Weboberfläche wird gebaut"
(cd web && npm ci && npm run build)
echo "Backend wird veröffentlicht"
VERSION="$(git describe --tags --always 2>/dev/null || echo dev)"
PUBLISH_DIR="$(mktemp -d)"
trap 'rm -rf "$PUBLISH_DIR"' EXIT
dotnet publish src/LittleSister.Api/LittleSister.Api.csproj --configuration Release --output "$PUBLISH_DIR" \
  -p:InformationalVersion="$VERSION"
cp -r web/build "$PUBLISH_DIR/wwwroot"
cp LICENSE THIRD-PARTY-NOTICES "$PUBLISH_DIR/"

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

# 7) systemd-Dienst: startet mit dem Server und nach einem Absturz neu
sudo tee /etc/systemd/system/little-sister.service >/dev/null <<EOF
[Unit]
Description=Little Sister (Kameras, Aufnahme, Live-Ansicht)
After=network-online.target
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
