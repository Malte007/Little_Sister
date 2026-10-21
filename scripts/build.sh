#!/usr/bin/env bash
# Baut Backend und Weboberfläche und führt alle Tests aus.
# Wird lokal und von GitHub/Gitea Actions gleichermaßen aufgerufen.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "::group::Weboberfläche"
pushd web >/dev/null
npm ci
npm run lint
npm run check
npm test
npm run build
popd >/dev/null
echo "::endgroup::"

echo "::group::Backend"
dotnet restore LittleSister.slnx
dotnet build LittleSister.slnx --configuration Release --no-restore
dotnet test LittleSister.slnx --configuration Release --no-build \
  --logger "trx" --results-directory artifacts/test-results
echo "::endgroup::"
