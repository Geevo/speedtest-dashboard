# syntax=docker/dockerfile:1.7

FROM node:22-bookworm-slim AS web-build
WORKDIR /source/web
COPY src/SpeedtestDashboard.Web/package.json src/SpeedtestDashboard.Web/package-lock.json ./
RUN npm ci
COPY src/SpeedtestDashboard.Web/ ./
RUN npm run build

FROM golang:1.27.1-bookworm AS librespeed-build
ARG TARGETARCH
ARG INSTALL_LIBRESPEED=false
ARG LIBRESPEED_VERSION=1.0.13
ARG LIBRESPEED_COMMIT=2f2408764d88e9601aa64a03b340f8e3151003e4
ARG LIBRESPEED_SOURCE_SHA256=5e6622697bf5b651d552458997856dcd7f86e07e4bd44508aaad1be238085b11
ENV CGO_ENABLED=0 \
    GOOS=linux
WORKDIR /source
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /out \
    && if [ "$INSTALL_LIBRESPEED" = "true" ]; then \
      case "$TARGETARCH" in \
        amd64) LIBRESPEED_GOARCH="amd64" ;; \
        arm64) LIBRESPEED_GOARCH="arm64" ;; \
        *) echo "Unsupported LibreSpeed target architecture: $TARGETARCH" >&2; exit 1 ;; \
      esac; \
      LIBRESPEED_SOURCE_URL="https://github.com/librespeed/speedtest-cli/archive/${LIBRESPEED_COMMIT}.tar.gz"; \
      curl --fail --location --silent --show-error "$LIBRESPEED_SOURCE_URL" --output /tmp/librespeed-source.tar.gz; \
      echo "$LIBRESPEED_SOURCE_SHA256  /tmp/librespeed-source.tar.gz" | sha256sum --check --strict -; \
      tar --extract --gzip --file /tmp/librespeed-source.tar.gz --strip-components=1 --directory /source; \
      test "$LIBRESPEED_VERSION" = "1.0.13"; \
      GOARCH="$LIBRESPEED_GOARCH" go build \
        -trimpath \
        -buildvcs=false \
        -ldflags="-s -w -X github.com/librespeed/speedtest-cli/defs.ProgName=librespeed-cli -X github.com/librespeed/speedtest-cli/defs.ProgVersion=v${LIBRESPEED_VERSION} -X github.com/librespeed/speedtest-cli/defs.BuildDate=2026-04-30T06:42:34Z" \
        -o /out/librespeed-cli \
        .; \
      cp LICENSE /out/LICENSE.librespeed-cli; \
      rm -f /tmp/librespeed-source.tar.gz; \
    elif [ "$INSTALL_LIBRESPEED" != "false" ]; then \
      echo "INSTALL_LIBRESPEED must be true or false" >&2; exit 1; \
    fi

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /source
COPY SpeedtestDashboard.sln ./
COPY Directory.Build.props ./
COPY src/SpeedtestDashboard.Api/SpeedtestDashboard.Api.csproj src/SpeedtestDashboard.Api/packages.lock.json src/SpeedtestDashboard.Api/
COPY src/SpeedtestDashboard.Core/SpeedtestDashboard.Core.csproj src/SpeedtestDashboard.Core/packages.lock.json src/SpeedtestDashboard.Core/
COPY src/SpeedtestDashboard.Infrastructure/SpeedtestDashboard.Infrastructure.csproj src/SpeedtestDashboard.Infrastructure/packages.lock.json src/SpeedtestDashboard.Infrastructure/
RUN dotnet restore src/SpeedtestDashboard.Api/SpeedtestDashboard.Api.csproj --locked-mode -p:AuditPipeline=true
COPY src/SpeedtestDashboard.Api/ src/SpeedtestDashboard.Api/
COPY src/SpeedtestDashboard.Core/ src/SpeedtestDashboard.Core/
COPY src/SpeedtestDashboard.Infrastructure/ src/SpeedtestDashboard.Infrastructure/
COPY --from=web-build /source/web/dist/ src/SpeedtestDashboard.Api/wwwroot/
RUN dotnet publish src/SpeedtestDashboard.Api/SpeedtestDashboard.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG TARGETARCH
ARG INSTALL_OOKLA=false
ARG OOKLA_PACKAGE_VERSION=1.2.0.84-1.ea6b6773cf
ARG OOKLA_AMD64_SHA256=35e084567a6388631fb10cf01e5e0d6b57a67d34ede2b72ba111b3d9164c8b94
ARG OOKLA_ARM64_SHA256=98e7de9db3bf181d08bc67e647bcfc71349c8014e387289c08e54e5c55d82f37
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data \
    && chown app:app /data
RUN if [ "$INSTALL_OOKLA" = "true" ]; then \
      case "$TARGETARCH" in \
        amd64) OOKLA_ARCH="amd64"; OOKLA_SHA256="$OOKLA_AMD64_SHA256" ;; \
        arm64) OOKLA_ARCH="arm64"; OOKLA_SHA256="$OOKLA_ARM64_SHA256" ;; \
        *) echo "Unsupported Ookla target architecture: $TARGETARCH" >&2; exit 1 ;; \
      esac; \
      OOKLA_PACKAGE="speedtest_${OOKLA_PACKAGE_VERSION}_${OOKLA_ARCH}.deb"; \
      OOKLA_URL="https://packagecloud.io/ookla/speedtest-cli/packages/debian/bookworm/${OOKLA_PACKAGE}/download.deb?distro_version_id=215"; \
      curl --fail --location --silent --show-error "$OOKLA_URL" --output /tmp/ookla-speedtest.deb; \
      echo "$OOKLA_SHA256  /tmp/ookla-speedtest.deb" | sha256sum --check --strict -; \
      dpkg --install /tmp/ookla-speedtest.deb; \
      rm -f /tmp/ookla-speedtest.deb; \
    elif [ "$INSTALL_OOKLA" != "false" ]; then \
      echo "INSTALL_OOKLA must be true or false" >&2; exit 1; \
    fi
COPY --from=librespeed-build /out/ /tmp/librespeed-dist/
RUN if [ -f /tmp/librespeed-dist/librespeed-cli ]; then \
      install --mode=0755 /tmp/librespeed-dist/librespeed-cli /usr/local/bin/librespeed-cli; \
      install --directory --mode=0755 /usr/share/licenses/librespeed-cli; \
      install --mode=0644 /tmp/librespeed-dist/LICENSE.librespeed-cli /usr/share/licenses/librespeed-cli/LICENSE; \
    fi \
    && rm -rf /tmp/librespeed-dist
WORKDIR /app
COPY --from=api-build --chown=app:app /app/publish/ ./
COPY --chown=app:app THIRD_PARTY_NOTICES.md /usr/share/doc/speedtest-dashboard/THIRD_PARTY_NOTICES.md
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    SpeedtestDashboard__DataDirectory=/data
EXPOSE 8080
VOLUME ["/data"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD ["curl", "--fail", "--silent", "--show-error", "http://127.0.0.1:8080/api/health"]
ENTRYPOINT ["dotnet", "SpeedtestDashboard.Api.dll"]
