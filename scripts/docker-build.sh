#!/usr/bin/env bash
# Baut das Docker-Image und schiebt es optional in eine Registry.
#   IMAGE   Name inkl. Registry, z. B. ghcr.io/malte007/little-sister (Standard: little-sister)
#   VERSION Tag des Images (Standard: dev)
#   PUSH    "true" schiebt das Image in die Registry
#   LATEST  "false": kein Tag "latest" (z. B. für Test-Versionen wie 0.1.2-test), Standard "true"
set -euo pipefail
cd "$(dirname "$0")/.."

IMAGE="$(echo "${IMAGE:-little-sister}" | tr '[:upper:]' '[:lower:]')"
VERSION="${VERSION:-dev}"
LATEST="${LATEST:-true}"
TAGS=(--tag "$IMAGE:$VERSION")
[[ "$LATEST" == "true" ]] && TAGS+=(--tag "$IMAGE:latest")

docker build \
  --build-arg VERSION="$VERSION" \
  "${TAGS[@]}" \
  .

if [[ "${PUSH:-false}" == "true" ]]; then
  docker push "$IMAGE:$VERSION"
  if [[ "$LATEST" == "true" ]]; then docker push "$IMAGE:latest"; fi
fi
