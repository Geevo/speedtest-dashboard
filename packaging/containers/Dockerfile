# syntax=docker/dockerfile:1.7

FROM node:22.23.2-bookworm-slim AS web-build
WORKDIR /source/web
COPY src/SpeedtestDashboard.Web/package.json src/SpeedtestDashboard.Web/package-lock.json ./
RUN npm ci
COPY src/SpeedtestDashboard.Web/ ./
RUN npm run build

FROM golang:1.27.1-bookworm AS librespeed-build
ARG TARGETARCH
# Defaults are pinned once in packaging/providers/build-librespeed.sh.
ARG LIBRESPEED_VERSION
ARG LIBRESPEED_COMMIT
ARG LIBRESPEED_SOURCE_SHA256
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
COPY packaging/providers/build-librespeed.sh /usr/local/bin/build-librespeed
RUN chmod 0755 /usr/local/bin/build-librespeed \
    && mkdir -p /out \
    && TARGETARCH="$TARGETARCH" \
      LIBRESPEED_VERSION="$LIBRESPEED_VERSION" \
      LIBRESPEED_COMMIT="$LIBRESPEED_COMMIT" \
      LIBRESPEED_SOURCE_SHA256="$LIBRESPEED_SOURCE_SHA256" \
      OUTPUT_DIR=/out \
      /usr/local/bin/build-librespeed

FROM golang:1.27.1-bookworm AS mlab-build
ARG TARGETARCH
# Defaults are pinned once in packaging/providers/build-mlab.sh.
ARG MLAB_VERSION
ARG MLAB_COMMIT
ARG MLAB_SOURCE_SHA256
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
COPY packaging/providers/build-mlab.sh /usr/local/bin/build-mlab
RUN chmod 0755 /usr/local/bin/build-mlab \
    && mkdir -p /out \
    && TARGETARCH="$TARGETARCH" \
      MLAB_VERSION="$MLAB_VERSION" \
      MLAB_COMMIT="$MLAB_COMMIT" \
      MLAB_SOURCE_SHA256="$MLAB_SOURCE_SHA256" \
      OUTPUT_DIR=/out \
      /usr/local/bin/build-mlab

FROM debian:bookworm-slim AS fastcom-fetch
ARG TARGETARCH
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
COPY packaging/providers/download-fast-cli.sh /usr/local/bin/download-fast-cli
RUN chmod 0755 /usr/local/bin/download-fast-cli \
    && TARGETARCH="$TARGETARCH" OUTPUT_DIR=/out /usr/local/bin/download-fast-cli

FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS api-build
ARG SOURCE_REVISION=unknown
WORKDIR /source
COPY SpeedtestDashboard.sln Directory.Build.props ./
COPY src/SpeedtestDashboard.Api/SpeedtestDashboard.Api.csproj src/SpeedtestDashboard.Api/packages.lock.json src/SpeedtestDashboard.Api/
COPY src/SpeedtestDashboard.Core/SpeedtestDashboard.Core.csproj src/SpeedtestDashboard.Core/packages.lock.json src/SpeedtestDashboard.Core/
COPY src/SpeedtestDashboard.Infrastructure/SpeedtestDashboard.Infrastructure.csproj src/SpeedtestDashboard.Infrastructure/packages.lock.json src/SpeedtestDashboard.Infrastructure/
RUN dotnet restore src/SpeedtestDashboard.Api/SpeedtestDashboard.Api.csproj --locked-mode -p:AuditPipeline=true
COPY src/SpeedtestDashboard.Api/ src/SpeedtestDashboard.Api/
COPY src/SpeedtestDashboard.Core/ src/SpeedtestDashboard.Core/
COPY src/SpeedtestDashboard.Infrastructure/ src/SpeedtestDashboard.Infrastructure/
COPY --from=web-build /source/web/dist/ /source/web-dist/
RUN dotnet publish src/SpeedtestDashboard.Api/SpeedtestDashboard.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:FrontendDist=/source/web-dist \
    -p:SourceRevisionId="$SOURCE_REVISION" \
    -p:DebugSymbols=false \
    -p:DebugType=None \
    -p:IsTransformWebConfigDisabled=true \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
ARG TARGETARCH
ARG APP_VERSION=0.0.0-local
ARG SOURCE_REVISION=unknown
ARG BUILD_DATE=unknown
ARG SOURCE_URL=""
ARG DOCUMENTATION_URL=""
ARG INSTALL_OOKLA=false
LABEL org.opencontainers.image.title="Speedtest Dashboard" \
      org.opencontainers.image.description="A self-hosted dashboard for testing a server or container network connection." \
      org.opencontainers.image.source="$SOURCE_URL" \
      org.opencontainers.image.documentation="$DOCUMENTATION_URL" \
      org.opencontainers.image.version="$APP_VERSION" \
      org.opencontainers.image.revision="$SOURCE_REVISION" \
      org.opencontainers.image.created="$BUILD_DATE" \
      org.opencontainers.image.licenses="MIT AND LGPL-3.0-only AND Apache-2.0"
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data /usr/share/doc/speedtest-dashboard /usr/share/licenses/speedtest-dashboard \
    && chown app:app /data
COPY packaging/providers/download-ookla.sh packaging/providers/OOKLA_NOTICE.md /tmp/ookla/
RUN set -eu; if [ "$INSTALL_OOKLA" = "true" ]; then \
      TARGETARCH="$TARGETARCH" OUTPUT_DIR=/tmp/ookla sh /tmp/ookla/download-ookla.sh; \
      dpkg --install /tmp/ookla/ookla-speedtest.deb; \
      cp /tmp/ookla/OOKLA_NOTICE.md /usr/share/doc/speedtest-dashboard/OOKLA_NOTICE.md; \
    elif [ "$INSTALL_OOKLA" != "false" ]; then \
      echo "INSTALL_OOKLA must be true or false" >&2; exit 1; \
    fi; \
    rm -rf /tmp/ookla
COPY --from=librespeed-build --chmod=0755 /out/librespeed-cli /usr/local/bin/librespeed-cli
COPY --from=librespeed-build --chmod=0644 /out/LICENSE.librespeed-cli /usr/share/licenses/librespeed-cli/LICENSE
COPY --from=fastcom-fetch --chmod=0755 /out/fast-cli /usr/local/bin/fast-cli
COPY --from=fastcom-fetch --chmod=0644 /out/LICENSE.fast-cli /usr/share/licenses/fast-cli/LICENSE
COPY --from=mlab-build --chmod=0755 /out/mlab-ndt7-client /usr/local/bin/mlab-ndt7-client
COPY --from=mlab-build --chmod=0644 /out/LICENSE.mlab-ndt7-client /usr/share/licenses/mlab-ndt7-client/LICENSE
WORKDIR /app
COPY --from=api-build --chown=app:app /app/publish/ ./
COPY THIRD_PARTY_NOTICES.md /usr/share/doc/speedtest-dashboard/THIRD_PARTY_NOTICES.md
COPY LICENSE /usr/share/licenses/speedtest-dashboard/LICENSE
USER app
ENV DASHBOARD_PORT=8080
EXPOSE 8080
VOLUME ["/data"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent --show-error "http://127.0.0.1:${DASHBOARD_PORT:-8080}/api/health"
ENTRYPOINT ["dotnet", "SpeedtestDashboard.Api.dll"]
