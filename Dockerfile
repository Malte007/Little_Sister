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

# 3) go2rtc für die Live-Ansicht (MIT-Lizenz); Version hier und in der CI gemeinsam anheben
FROM alexxit/go2rtc:1.9.14 AS go2rtc

# 4) Schlankes Laufzeit-Image mit ffmpeg für die Aufnahme und go2rtc für die Live-Ansicht
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*
COPY --from=go2rtc /usr/local/bin/go2rtc /usr/local/bin/go2rtc
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
# 8080: Weboberfläche und API, 8555 (TCP+UDP): WebRTC für die Live-Ansicht
EXPOSE 8080 8555/tcp 8555/udp
ENTRYPOINT ["dotnet", "LittleSister.Api.dll"]
