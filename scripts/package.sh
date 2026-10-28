#!/usr/bin/env bash
# Baut das Release-Paket little-sister.tar.gz (framework-abhängig: braucht auf dem Server die ASP.NET-Core-Laufzeit 10).
# Wird von der CI für GitHub-Releases und von install-ubuntu.sh beim Bauen aus dem Quellcode genutzt.
#   VERSION  Versionsangabe (Standard: git describe)
#   OUT      Zielordner (Standard: artifacts)
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${VERSION:-$(git describe --tags --always 2>/dev/null || echo dev)}"
OUT="${OUT:-artifacts}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

echo "Weboberfläche wird gebaut"
(cd web && npm ci && npm run build)

echo "Backend wird veröffentlicht ($VERSION)"
dotnet publish src/LittleSister.Api/LittleSister.Api.csproj --configuration Release --output "$STAGE/little-sister" \
  -p:InformationalVersion="$VERSION" -p:UseAppHost=false
cp -r web/build "$STAGE/little-sister/wwwroot"
cp LICENSE THIRD-PARTY-NOTICES "$STAGE/little-sister/"
echo "$VERSION" > "$STAGE/little-sister/VERSION"

mkdir -p "$OUT"
tar -czf "$OUT/little-sister.tar.gz" -C "$STAGE" little-sister
(cd "$OUT" && sha256sum little-sister.tar.gz > little-sister.tar.gz.sha256)
cp scripts/install-ubuntu.sh "$OUT/"
echo "Paket: $OUT/little-sister.tar.gz"
