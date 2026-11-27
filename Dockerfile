# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/Aggregatarr.Web/Aggregatarr.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Unraid's nobody:users is 99:100. The name does not matter; the ids do.
RUN set -eux; \
    if ! getent group 100 >/dev/null; then groupadd -g 100 users; fi; \
    if ! id -u 99 >/dev/null 2>&1; then useradd -u 99 -g 100 -M -d /config -s /usr/sbin/nologin appuser; fi; \
    mkdir -p /config; \
    chown -R 99:100 /config /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    AGGREGATARR_CONFIG=/config

USER 99:100
EXPOSE 8080
VOLUME ["/config"]
# The runtime image does not ship curl or wget. This reuses the app to GET /healthz.
HEALTHCHECK --interval=30s --timeout=5s --start-period=25s --retries=3 \
  CMD ["dotnet", "Aggregatarr.Web.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Aggregatarr.Web.dll"]

LABEL org.opencontainers.image.source="https://github.com/007darkmatter5/Aggregatarr" \
      org.opencontainers.image.title="Aggregatarr" \
      org.opencontainers.image.description="One-pane dashboard for Sonarr, Radarr, Lidarr, Prowlarr, Bazarr, qBittorrent and SABnzbd" \
      org.opencontainers.image.licenses="MIT"
