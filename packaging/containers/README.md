# Docker and Podman

The same image runs on Docker and rootless Podman on `linux/amd64` and `linux/arm64`. LibreSpeed is already inside it, so there is no helper database or second app to babysit.

Replace `<owner>` with the GitHub repository owner. Release builds are published as `ghcr.io/<owner>/speedtest-dashboard`.

## Docker Compose

The quickest route is the Compose file already in this repo:

```bash
export SPEEDTEST_DASHBOARD_IMAGE=ghcr.io/<owner>/speedtest-dashboard:latest
export DASHBOARD_PORT=8008
docker compose -f packaging/containers/compose.yml up -d
docker compose -f packaging/containers/compose.yml logs -f
```

Or drop this into your own `compose.yml`:

```yaml
services:
  speedtest-dashboard:
    image: ghcr.io/<owner>/speedtest-dashboard:latest
    container_name: speedtest-dashboard
    restart: unless-stopped
    ports:
      - "8008:8008"
    environment:
      DASHBOARD_PORT: "8008"
    volumes:
      - speedtest-data:/data
    read_only: true
    tmpfs:
      - /tmp:size=64m,mode=1777
    cap_drop:
      - ALL
    security_opt:
      - no-new-privileges:true

volumes:
  speedtest-data:
```

Start it with `docker compose up -d`, then open `http://<docker-host>:8008`. Change both occurrences of `8008` to use another free port.

Fresh installs allow anonymous access. Put the dashboard behind HTTPS before enabling login protection. If this really is an isolated, trusted, HTTP-only LAN, add the following environment setting:

```yaml
environment:
  Authentication__AllowInsecureHttp: "true"
```

That exception is only needed for login; anonymous mode works over HTTP without it.

## Plain Docker

```bash
docker volume create speedtest-data

docker run -d \
  --name speedtest-dashboard \
  --restart unless-stopped \
  -p 8008:8008 \
  -e DASHBOARD_PORT=8008 \
  -v speedtest-data:/data \
  --read-only \
  --tmpfs /tmp:size=64m,mode=1777 \
  --cap-drop ALL \
  --security-opt no-new-privileges \
  ghcr.io/<owner>/speedtest-dashboard:latest
```

Useful checks while getting settled:

```bash
docker logs -f speedtest-dashboard
curl -fsS http://127.0.0.1:8008/api/health
docker inspect speedtest-dashboard --format '{{json .Mounts}}'
```

## Routing tests through Gluetun

Speed tests run inside the dashboard container. When it shares Gluetun's network, the tests and public-IP checks use Gluetun's VPN connection.

### Same Compose project

Add the dashboard beside your existing Gluetun service:

```yaml
services:
  gluetun:
    image: qmcgaw/gluetun:latest
    container_name: gluetun
    # Keep your normal cap_add, /dev/net/tun, VPN credentials and other settings.
    ports:
      - "8008:8008" # Speedtest Dashboard

  speedtest-dashboard:
    image: ghcr.io/<owner>/speedtest-dashboard:latest
    container_name: speedtest-dashboard
    network_mode: "service:gluetun"
    restart: unless-stopped
    environment:
      DASHBOARD_PORT: "8008"
    volumes:
      - speedtest-data:/data
    read_only: true
    tmpfs:
      - /tmp:size=64m,mode=1777
    cap_drop:
      - ALL
    security_opt:
      - no-new-privileges:true

volumes:
  speedtest-data:
```

There is intentionally no `ports` block on `speedtest-dashboard`. Gluetun owns the shared network stack, so Docker publishes the dashboard port from the `gluetun` service.

The dashboard does not need `NET_ADMIN` or access to `/dev/net/tun`; those stay with Gluetun.

### Gluetun in another Compose project

Give Gluetun a stable container name and publish the dashboard port there:

```yaml
services:
  gluetun:
    container_name: gluetun
    ports:
      - "8008:8008"
```

Then use the running container's network namespace in the dashboard project:

```yaml
services:
  speedtest-dashboard:
    image: ghcr.io/<owner>/speedtest-dashboard:latest
    container_name: speedtest-dashboard
    network_mode: "container:gluetun"
    restart: unless-stopped
    environment:
      DASHBOARD_PORT: "8008"
    volumes:
      - speedtest-data:/data
    read_only: true
    tmpfs:
      - /tmp:size=64m,mode=1777
    cap_drop:
      - ALL
    security_opt:
      - no-new-privileges:true

volumes:
  speedtest-data:
```

Start Gluetun first. Docker cannot add a published port to an existing container, so changing its `ports` list means recreating the Gluetun container.

### Plain Docker with an existing Gluetun container

After publishing `8008:8008` on the container named `gluetun`:

```bash
docker volume create speedtest-data

docker run -d \
  --name speedtest-dashboard \
  --restart unless-stopped \
  --network container:gluetun \
  -e DASHBOARD_PORT=8008 \
  -v speedtest-data:/data \
  --read-only \
  --tmpfs /tmp:size=64m,mode=1777 \
  --cap-drop ALL \
  --security-opt no-new-privileges \
  ghcr.io/<owner>/speedtest-dashboard:latest
```

Again, there is no `-p` flag on this command. The port is published by Gluetun.

### Choosing another port

Port `8080` is common, and every container sharing Gluetun's network must listen on a different internal port. `DASHBOARD_PORT` lets you choose one that is free:

```yaml
services:
  gluetun:
    ports:
      - "8008:8008"

  speedtest-dashboard:
    network_mode: "service:gluetun"
    environment:
      DASHBOARD_PORT: "8008"
```

Then open `http://<docker-host>:8008`. Use the same free port in both places; the image health check follows `DASHBOARD_PORT` automatically.

For a standalone container there is another option: leave the dashboard listening on its default `8080` and publish a different host port with `8008:8080`. That does not work around an internal clash in a shared Gluetun network, which is why `DASHBOARD_PORT` exists.

Once it is up, the Network panel should show the VPN exit you expect. That proves what address the dashboard observed; it does not replace Gluetun's kill switch or leak testing.

Gluetun's own docs cover the same networking model in [Connect a container to Gluetun](https://github.com/qdm12/gluetun-wiki/blob/main/setup/connect-a-container-to-gluetun.md) and [Port mapping](https://github.com/qdm12/gluetun-wiki/blob/main/setup/port-mapping.md).

## Podman

Rootless Podman can run the same image:

```bash
podman volume create speedtest-data

podman run -d \
  --name speedtest-dashboard \
  -p 8008:8008 \
  -e DASHBOARD_PORT=8008 \
  -v speedtest-data:/data:Z \
  --read-only \
  --tmpfs /tmp:rw,size=64m,mode=1777 \
  --cap-drop ALL \
  --security-opt no-new-privileges \
  ghcr.io/<owner>/speedtest-dashboard:latest
```

With a Compose provider installed, replace `docker compose` with `podman compose` or `podman-compose`.

## Build it locally

From the repository root:

```bash
DASHBOARD_PORT=8008 docker compose -f packaging/containers/compose.local.yml up --build -d
```

That builds `speedtest-dashboard:local` and starts it. The local build excludes Ookla by default. Add `ALLOW_INSECURE_HTTP=true` to the command if you need to test login over trusted local HTTP.

To build without starting anything:

```bash
docker build -f packaging/containers/Dockerfile -t speedtest-dashboard:local .
# or
podman build -f packaging/containers/Dockerfile -t speedtest-dashboard:local .
```

See [Enable Ookla](../../docs/ookla.md) if you want a local image containing the proprietary CLI.

## Storage, backups, and updates

Everything worth keeping lives in `/data`:

- SQLite results and schedules
- login configuration
- the encrypted machine API credential
- Data Protection keys used for sessions and API-key encryption

The image runs as a non-root user with a read-only root filesystem, a writable `/data`, tmpfs `/tmp`, all capabilities dropped, and no-new-privileges. Preserve the entire data volume and never mount one SQLite database into two running dashboard instances.

The repo Compose files use project name `speedtest-dashboard`, so their default volume is `speedtest-dashboard_speedtest-data`. If you are migrating from the old root-level Compose files, use the previous project name on every command so Compose keeps using the same volume:

```bash
docker volume ls
docker compose -p <previous-project> -f packaging/containers/compose.yml up -d
```

The old project name is often `speedtest-app`. Check before switching rather than accidentally starting with a fresh database.

For a filesystem-level backup, stop the container so SQLite and its WAL file are consistent:

```bash
docker stop speedtest-dashboard
# Copy or snapshot the volume with your normal backup tooling.
docker start speedtest-dashboard
```

To update a Compose install:

```bash
export SPEEDTEST_DASHBOARD_IMAGE=ghcr.io/<owner>/speedtest-dashboard:<new-tag>
docker compose -f packaging/containers/compose.yml pull
docker compose -f packaging/containers/compose.yml up -d
```

Database migrations run before the web server starts. Back up before each version change; downgrades are not supported. Published tags include the full semantic version, major/minor, major, `latest`, and `sha-<short>`. Moving stable tags advance only for stable releases.

[Back to the main README](../../README.md) · [Configuration](../../docs/configuration.md)
