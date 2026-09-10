# Docker and Podman

The same OCI image runs on Docker and rootless Podman, on `linux/amd64` and
`linux/arm64`. It includes the application and LibreSpeed CLI. For local Ookla
support, see [Enable Ookla](../../docs/ookla.md).

Run all commands from the repository root.

## Docker: published image

Replace `<owner>` with the repository owner. Public image names are set by the
release workflow to `ghcr.io/<owner>/speedtest-dashboard`.

```bash
export SPEEDTEST_DASHBOARD_IMAGE=ghcr.io/<owner>/speedtest-dashboard:0.12.0-rc.1
docker compose -f packaging/containers/compose.yml up -d
```

Open `http://localhost:8080`. Fresh installations allow anonymous access.
Put the dashboard behind HTTPS before configuring login protection. On a
trusted local network, `Authentication__AllowInsecureHttp=true` permits login
over HTTP.

## Podman: published image

```bash
podman volume create speedtest-data
podman run -d --name speedtest-dashboard \
  -p 8080:8080 \
  -v speedtest-data:/data:Z \
  --read-only \
  --tmpfs /tmp:rw,size=64m,mode=1777 \
  --cap-drop ALL \
  --security-opt no-new-privileges \
  ghcr.io/<owner>/speedtest-dashboard:0.12.0-rc.1
```

With a Compose provider installed, Podman can also use the Compose files:
replace `docker compose` with `podman compose` (or `podman-compose`).

## Build from source

```bash
docker compose -f packaging/containers/compose.local.yml up --build -d
```

This standalone Compose file builds `speedtest-dashboard:local` from the root
context. It defaults to excluding Ookla, with both acceptance settings false.
Set `ALLOW_INSECURE_HTTP=true` on the command for login testing over trusted
local HTTP. It is not needed for anonymous use.

To build without starting a container:

```bash
docker build -f packaging/containers/Dockerfile -t speedtest-dashboard:local .
# Or:
podman build -f packaging/containers/Dockerfile -t speedtest-dashboard:local .
```

## Storage, hardening, and updates

Both Compose files run as the image's non-root user, with a read-only root
filesystem, writable `/data`, tmpfs `/tmp`, all capabilities dropped, and
no-new-privileges. State includes SQLite and Data Protection keys; preserve the
whole volume. Stop the service before a filesystem copy, and back up before
every version change. Do not run two instances against the same SQLite file.

Both files use project name `speedtest-dashboard`, so their default volume is
`speedtest-dashboard_speedtest-data`. If migrating from the old root-level
Compose files, pass `-p <previous-project>` to every Compose command to retain
the existing volume (the old project name is often `speedtest-app`). Check
`docker volume ls` or `podman volume ls` before switching. The direct Podman
example uses its separately named `speedtest-data` volume.

To update a published-image installation, set the new image tag and run:

```bash
docker compose -f packaging/containers/compose.yml pull
docker compose -f packaging/containers/compose.yml up -d
```

Database migrations run before HTTP startup. Downgrades are not supported.
Release tags are full semantic version, major.minor, major, `latest`, and
`sha-<short>`; moving stable tags update only for stable releases.

[Deployment overview](../README.md) · [Configuration](../../README.md#configuration)
