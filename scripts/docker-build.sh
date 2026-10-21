#!/usr/bin/env bash
# Baut das Docker-Image und schiebt es optional in eine Registry.
#   IMAGE   Name inkl. Registry, z. B. ghcr.io/malte007/little-sister (Standard: little-sister)
#   VERSION Tag des Images (Standard: dev)
#   PUSH    "true" schiebt das Image samt Tag "latest" in die Registry
set -euo pipefail
cd "$(dirname "$0")/.."

IMAGE="$(echo "${IMAGE:-little-sister}" | tr '[:upper:]' '[:lower:]')"
VERSION="${VERSION:-dev}"

docker build \
  --build-arg VERSION="$VERSION" \
  --tag "$IMAGE:$VERSION" \
  --tag "$IMAGE:latest" \
  .

if [[ "${PUSH:-false}" == "true" ]]; then
  docker push "$IMAGE:$VERSION"
  docker push "$IMAGE:latest"
fi
