# Speedtest Dashboard

A self-hosted dashboard for testing the network connection of a server or container. It includes scheduled tests, saved results, statistics, network identity, LibreSpeed, optional Ookla support, and a small API.

Tests and public-IP lookups run from the backend. The displayed IP is the backend's observed egress; it does not prove that a VPN is active.

## Screenshots

Screenshots use a temporary database with sanitized sample data. The IP address shown is from the documentation-only `203.0.113.0/24` range.

### Overview

![Overview with network identity, seven-day summary, latest result, and next schedule](docs/screenshots/overview.png)

### Statistics

![Seven-day statistics with provider filters and summary measurements](docs/screenshots/statistics.png)

### Schedules

![Enabled and disabled recurring schedules](docs/screenshots/schedules.png)

### Mobile results

![Results displayed as a mobile card](docs/screenshots/results-mobile.png)

## Features

- LibreSpeed tests with automatic or explicit server selection
- optional local Ookla CLI integration
- one active bandwidth test, a bounded queue, cancellation, and live status updates
- persistent SQLite results with filters, pagination, details, and deletion
- 24-hour, 7-day, 30-day, 90-day, and all-time statistics
- one-off, interval, daily, and weekly schedules with IANA time zones
- backend-observed IPv4 and IPv6 identity with optional IPConfig.io metadata
- optional single-operator login and an independent machine API key
- light, dark, and system themes
- OCI images for `linux/amd64` and `linux/arm64`
- native Debian-based Proxmox LXC templates for amd64 and arm64

## Quick start

Published images use `ghcr.io/<owner>/speedtest-dashboard`. Replace `<owner>` with the repository owner until the final package name is set.

```bash
export SPEEDTEST_DASHBOARD_IMAGE=ghcr.io/<owner>/speedtest-dashboard:0.12.0-rc.1
docker compose up -d
```

Open `http://localhost:8080`. New installations allow anonymous access. Configure login protection in Settings after placing the dashboard behind HTTPS. On an isolated trusted network, `Authentication__AllowInsecureHttp=true` permits login over HTTP.

## Persistent storage

The container is disposable. Mount `/data` if you want state to survive updates.

`/data` contains:

- SQLite, including Results and schedule records
- login state and the protected API credential
- Data Protection keys used for sessions and API-key encryption

The production Compose file uses the `speedtest-data` named volume. Do not run two dashboard instances against the same SQLite file.

For native Proxmox LXC, durable state lives at `/var/lib/speedtest-dashboard`, not `/data`.

## Authentication

Login protection is off on a fresh database. Settings can create one local account and turn protection on or off. Turning protection off deletes the local account and password, so turning it on again creates fresh credentials. There is no registration, account list, email recovery, or role system.

Usernames are 3–64 characters using letters, digits, `.`, `_`, or `-`. Passwords are 6–128 characters with no composition rule. Five failed logins lock the account for 15 minutes; login requests are also limited to 10 per minute per source IP.

Browser sessions use an `HttpOnly`, `SameSite=Lax` cookie with a 12-hour sliding lifetime. Cookies are Secure unless `Authentication__AllowInsecureHttp=true`. Unsafe cookie-authenticated requests use an antiforgery token. Anonymous HTTP dashboards can also obtain antiforgery tokens; credential setup and login still require HTTPS unless the insecure-HTTP override is enabled. Health and session bootstrap routes remain anonymous.

The machine API uses a separate instance-wide bearer key. Generate, view, rotate, or revoke it in Settings. A dashboard session does not authenticate `/api/v1`, and an API key does not sign in to the dashboard.

## Providers

### LibreSpeed

Published OCI images and Proxmox templates include LibreSpeed CLI `1.0.13`, built from commit `2f2408764d88e9601aa64a03b340f8e3151003e4`. The source archive is checksum-verified and the LGPL-3.0 license is installed with the binary. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Tests run with `--json --no-icmp --secure`. The application does not enable result sharing or telemetry. Server discovery uses LibreSpeed's public HTTPS catalogue and caches results for five minutes.

### Ookla

Ookla support is available, but published images and LXC templates do not contain the official CLI. The project does not grant redistribution rights for it.

After reviewing Ookla's terms, build a local OCI image with the pinned CLI package:

```bash
docker build \
  --build-arg INSTALL_OOKLA=true \
  -t speedtest-dashboard:local .
```

Then set both runtime acceptance values:

```yaml
environment:
  Providers__Ookla__AcceptLicense: "true"
  Providers__Ookla__AcceptGdpr: "true"
```

The Dockerfile pins Ookla CLI `1.2.0.84-1.ea6b6773cf` and verifies separate amd64 and arm64 package checksums. Acceptance remains explicit and is passed to the CLI as `--accept-license` and `--accept-gdpr`.

## Scheduling

Schedules can run once, every 1–10080 minutes, daily, or weekly. Daily and weekly schedules use their saved IANA time zone and account for daylight-saving transitions.

The scheduler checks every 30 seconds by default and submits through the same bounded queue as manual and API tests. A due run is recorded as skipped when the queue is full, the previous run is still active, or its provider selection is no longer valid. It is not retried. Occurrences missed while the application was offline are recorded once and are not replayed; the next future occurrence is calculated instead.

## Results and statistics

Results contains individual terminal test records. `/results` is its browser route; `/history` redirects there for old bookmarks. Backend History route names remain available for compatibility.

Statistics owns aggregate analysis: summary values, prior-period trends, throughput charts, latency and jitter charts, and provider comparison. Supported ranges are `24h`, `7d` (default), `30d`, `90d`, and `all`.

History list requests default to 50 records and allow up to 200. They support provider, terminal status, UTC start/end, and opaque cursor filters. There is no automatic retention or bulk delete.

## API

Send the key as `Authorization: Bearer <api-key>`. Reads are limited to 120 requests per minute per source IP; writes are limited to 10. Queue capacity is enforced separately.

| Method and route | Purpose |
| --- | --- |
| `GET /api/health` | Anonymous service health and version |
| `GET /api/v1/network` | Backend IPv4 and IPv6 identity |
| `GET /api/v1/providers` | Provider health and capabilities |
| `GET /api/v1/providers/{id}` | One provider |
| `GET /api/v1/providers/{id}/servers` | Provider server search |
| `POST /api/v1/tests` | Queue a test |
| `GET /api/v1/tests/{id}` | Poll a test |
| `GET /api/v1/tests/{id}/events` | Stream test status events |
| `POST /api/v1/tests/{id}/cancel` | Cancel a test |
| `GET /api/v1/history` | List terminal results |
| `GET /api/v1/history/{id}` | Read one result |
| `GET /api/v1/statistics` | Read aggregate statistics |
| `GET /api/v1/schedules` | List schedules |
| `GET /api/v1/schedules/{id}` | Read one schedule |

`POST /api/v1/tests` accepts `{ "providerId": "librespeed", "serverId": null }`. An optional `Idempotency-Key` of at most 128 characters is retained for 24 hours. Reusing it with the same request returns the original job, including concurrent retries; changing the request returns `409`.

The `/api/v1` surface does not expose History deletion, schedule mutation, or authentication administration.

## Docker

The same OCI image supports Docker and Podman. The normal image includes the dashboard and LibreSpeed CLI, but not Ookla CLI.

`docker-compose.yml` is a hardened production example: non-root, read-only root filesystem, writable `/data`, tmpfs `/tmp`, all capabilities dropped, and no-new-privileges enabled. It does not use the Docker socket, privileged mode, `NET_ADMIN`, or host networking.

For a local source build:

```bash
docker compose -f docker-compose.dev.yml up --build -d
```

Release tags are `latest`, major, major.minor, full semantic version, and `sha-<short>`. `latest` moves only for stable releases. GHCR is the primary registry; mirrors must use the same manifest and digest.

## Podman

Rootless Podman consumes the same published image:

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

The named volume preserves state when the container is replaced.

## Proxmox LXC

Proxmox uses a native LXC appliance. It does not run Docker or Podman and is not built from OCI layers. The application runs directly under systemd as the `speedtest` user in an unprivileged container.

Release assets are named `speedtest-dashboard_<version>_amd64.tar.zst` and `speedtest-dashboard_<version>_arm64.tar.zst`. After verifying `SHA256SUMS`, copy the matching archive to Proxmox template storage and create the container:

```bash
wget <release-url>/speedtest-dashboard_0.12.0-rc.1_amd64.tar.zst \
  -O /var/lib/vz/template/cache/speedtest-dashboard_0.12.0-rc.1_amd64.tar.zst

pct create 120 \
  local:vztmpl/speedtest-dashboard_0.12.0-rc.1_amd64.tar.zst \
  --hostname speedtest-dashboard \
  --unprivileged 1 \
  --cores 2 \
  --memory 1024 \
  --swap 512 \
  --net0 name=eth0,bridge=vmbr0,ip=dhcp \
  --start 1
```

The target for this release is Proxmox VE 9 with a Debian 12 root filesystem. Final support requires the release candidate to pass the real-host checklist in [packaging/proxmox/README.md](packaging/proxmox/README.md); archive creation alone is not runtime validation.

Inside the container:

```bash
systemctl status speedtest-dashboard
journalctl -u speedtest-dashboard
curl -fsS http://127.0.0.1:8080/api/health
```

The dashboard listens on port 8080. Configure Proxmox firewall access yourself. The appliance does not change Proxmox networking, DNS, time, or firewall settings and does not install SSH. Use `pct console` or `pct enter` for administration.

New templates are intended for new installations. Before attempting a manual in-place update, back up the container and `/var/lib/speedtest-dashboard`. A native package/update mechanism is not part of this release.

## Configuration

Environment variables use double underscores for nested keys.

| Variable | Default |
| --- | --- |
| `Authentication__AllowInsecureHttp` | `false` |
| `Authentication__DataProtectionPath` | `/data/dataprotection` |
| `ReverseProxy__TrustForwardedHeaders` | `false` |
| `Storage__DatabasePath` | `/data/speedtest.db` |
| `Storage__CommandTimeoutSeconds` | `10` |
| `Scheduler__PollIntervalSeconds` | `30` |
| `SpeedTests__QueueCapacity` | `4` |
| `SpeedTests__QueueFullRetryAfterSeconds` | `5` |
| `SpeedTests__SseHeartbeatSeconds` | `20` |
| `Processes__DefaultTimeoutSeconds` | `120` |
| `Processes__MaxTimeoutSeconds` | `600` |
| `Processes__DefaultStdoutLimitBytes` | `2097152` |
| `Processes__DefaultStderrLimitBytes` | `1048576` |
| `Processes__AbsoluteOutputLimitBytes` | `8388608` |
| `NetworkIdentity__SuccessCacheSeconds` | `300` |
| `NetworkIdentity__FailureCacheSeconds` | `30` |
| `NetworkIdentity__RefreshThrottleSeconds` | `10` |
| `NetworkIdentity__RequestTimeoutSeconds` | `5` |
| `NetworkIdentity__MetadataProvider` | `none` |
| `Providers__LibreSpeed__Enabled` | `true` |
| `Providers__LibreSpeed__ExecutablePath` | `/usr/local/bin/librespeed-cli` (OCI) |
| `Providers__LibreSpeed__HealthTimeoutSeconds` | `5` |
| `Providers__LibreSpeed__HealthCacheSeconds` | `45` |
| `Providers__LibreSpeed__TestTimeoutSeconds` | `180` |
| `Providers__LibreSpeed__ServerListTimeoutSeconds` | `20` |
| `Providers__LibreSpeed__ServerCacheSeconds` | `300` |
| `Providers__LibreSpeed__MaximumServers` | `250` |
| `Providers__LibreSpeed__DisableIcmp` | `true` |
| `Providers__LibreSpeed__PreferHttps` | `true` |
| `Providers__Ookla__Enabled` | `true` |
| `Providers__Ookla__ExecutablePath` | `/usr/bin/speedtest` |
| `Providers__Ookla__AcceptLicense` | `false` |
| `Providers__Ookla__AcceptGdpr` | `false` |
| `Providers__Ookla__HealthTimeoutSeconds` | `5` |
| `Providers__Ookla__HealthCacheSeconds` | `45` |
| `Providers__Ookla__TestTimeoutSeconds` | `180` |
| `Providers__Ookla__ServerListTimeoutSeconds` | `30` |
| `Providers__Ookla__ServerCacheSeconds` | `300` |
| `Providers__Ookla__MaximumServers` | `100` |

The LXC environment overrides the storage paths and LibreSpeed executable path for its filesystem layout. Configuration validation rejects unsafe or out-of-range values at startup.

When `ReverseProxy__TrustForwardedHeaders=true`, the app clears ASP.NET Core's known-proxy restrictions. Enable it only when untrusted clients cannot reach port 8080 directly and the proxy replaces forwarded headers.

### IP metadata

`NetworkIdentity__MetadataProvider` defaults to `none`, which discovers IPv4 and IPv6 addresses without any enrichment lookup. Set it to `ipconfig` to annotate each discovered address using [IPConfig.io](https://ipconfig.io), which needs no account or token.

The response supplies the country, country code, AS number, and AS organization. Region and city are included only when the address database can place the address: ordinary ISP addresses usually resolve to one, while anycast and datacenter addresses often do not. The dashboard shows those fields when they are present and leaves them empty otherwise; it never infers them. IPConfig.io reports no ISP separate from the AS organization, and no metadata field is used to claim VPN status.

Each address is looked up separately over HTTPS, results are cached with the identity snapshot, and a failed or slow lookup leaves address discovery intact. Enabling this sends each discovered public IP address to IPConfig.io.

Installations that used the removed IPinfo Lite provider should replace `NetworkIdentity__MetadataProvider=ipinfo` with `ipconfig` and drop `NetworkIdentity__Ipinfo__Token`; `ipinfo` is no longer a valid selection and fails validation at startup. Results already stored keep the source label recorded when they were written.

## Backup and upgrade

Stop the container before copying `/data` so SQLite and its WAL files are consistent. For LXC, use Proxmox backup/snapshot tooling or stop the service before copying `/var/lib/speedtest-dashboard`.

Keep the durable path mounted while replacing the OCI container. Database migrations run before HTTP startup and preserve existing state. Back up before every version change; downgrades are not supported.

## Security

Speed-test requests accept only a provider ID and validated provider-owned server ID. Executable paths, URLs, command strings, and arbitrary flags are never accepted from HTTP. Child processes use literal argument lists without a shell, bounded output, timeouts, cancellation, and process-tree termination.

Raw provider output and secrets are not returned by the API. See [SECURITY.md](SECURITY.md) for vulnerability reporting.

## Licensing

Speedtest Dashboard is licensed under [MIT](LICENSE). LibreSpeed CLI remains LGPL-3.0. Ookla CLI is proprietary and is not included in public artifacts. Provider and platform names are descriptive; this project is not affiliated with Ookla, Speedtest.net, LibreSpeed, Docker, Podman, or Proxmox.

## Development

Prerequisites are .NET SDK 10, Node.js 22+, and npm.

```bash
mkdir -p .data
Storage__DatabasePath="$PWD/.data/speedtest.db" \
Authentication__DataProtectionPath="$PWD/.data/dataprotection" \
Authentication__AllowInsecureHttp=true \
  dotnet run --project src/SpeedtestDashboard.Api --urls http://localhost:5080
```

In another terminal:

```bash
npm ci --prefix src/SpeedtestDashboard.Web
npm run dev --prefix src/SpeedtestDashboard.Web
```

Vite serves `http://localhost:5173` and proxies `/api` to the backend. See [CONTRIBUTING.md](CONTRIBUTING.md) for the verification commands and provider safety rules. [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) records the current implementation boundaries without duplicating release history.
