FROM node:22.23.2-bookworm-slim AS node

FROM golang:1.27.1-bookworm AS go

FROM mcr.microsoft.com/dotnet/sdk:10.0.400

COPY --from=node /usr/local/ /usr/local/
COPY --from=go /usr/local/go/ /usr/local/go/

ENV PATH="/usr/local/go/bin:${PATH}"

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
      debian-archive-keyring \
      debootstrap \
      qemu-user-static \
      zstd \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /source
