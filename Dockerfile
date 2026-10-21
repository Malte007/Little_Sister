# syntax=docker/dockerfile:1

# 1) Weboberfläche bauen
FROM node:24-bookworm-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# 2) Backend veröffentlichen
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG VERSION=0.1.0
WORKDIR /src
COPY LittleSister.slnx Directory.Build.props Directory.Packages.props ./
COPY src/LittleSister.Core/LittleSister.Core.csproj src/LittleSister.Core/
COPY src/LittleSister.Infrastructure/LittleSister.Infrastructure.csproj src/LittleSister.Infrastructure/
COPY src/LittleSister.Api/LittleSister.Api.csproj src/LittleSister.Api/
RUN dotnet restore src/LittleSister.Api/LittleSister.Api.csproj
COPY src/ src/
RUN dotnet publish src/LittleSister.Api/LittleSister.Api.csproj \
      --configuration Release --no-restore --output /app \
      -p:Version="$VERSION"

# 3) Schlankes Laufzeit-Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=backend /app ./
COPY --from=web /src/web/build ./wwwroot

ENV ASPNETCORE_HTTP_PORTS=8080 \
    LittleSister__DataPath=/data \
    LittleSister__DefaultRecordingsPath=/recordings

# Ordner für Datenbank/Schlüssel und Aufnahmen; gehören dem unprivilegierten Benutzer "app"
RUN mkdir -p /data /recordings && chown app:app /data /recordings
VOLUME ["/data", "/recordings"]

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "LittleSister.Api.dll"]
