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

## Deployment

Docker/Podman and native Proxmox LXC are equally supported packaging targets. Both include LibreSpeed and use the same application source.

| Target | Start here | Runtime and persistent state |
| --- | --- | --- |
| Docker or rootless Podman | [Container guide](packaging/containers/README.md) | OCI image, `/data` volume |
| Proxmox LXC | [Native LXC guide](packaging/proxmox/README.md) | Debian + systemd, `/var/lib/speedtest-dashboard` |

For Docker, run from the repository root (replace `<owner>` with the repository owner):

```bash
export SPEEDTEST_DASHBOARD_IMAGE=ghcr.io/<owner>/speedtest-dashboard:0.12.0-rc.1
docker compose -f packaging/containers/compose.yml up -d
```

For Proxmox, download the matching release template and follow the native LXC guide to create an unprivileged container.

Open `http://<server-address>:8080`. New installations allow anonymous access. Configure login protection in Settings after placing the dashboard behind HTTPS. On an isolated trusted network, `Authentication__AllowInsecureHttp=true` permits login over HTTP.

## Persistent storage

The container is disposable. Mount `/data` if you want state to survive updates.

`/data` contains:

- SQLite, including Results and schedule records
- login state and the protected API credential
- Data Protection keys used for sessions and API-key encryption

The production [Compose file](packaging/containers/compose.yml) uses the `speedtest-data` named volume. Do not run two dashboard instances against the same SQLite file.

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

The integration is included in the application; the official CLI is excluded from all public images and LXC templates. Local builds can add the pinned, checksum-verified CLI with one opt-in flag. Runtime use requires explicit licence and GDPR acceptance.

See [Enable Ookla](docs/ookla.md) for complete Docker, Podman, and native Proxmox commands.

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
| `NetworkIdentity__MetadataProvider` | `ipconfig` |
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

`NetworkIdentity__MetadataProvider` defaults to `ipconfig`, which annotates each discovered address using [IPConfig.io](https://ipconfig.io) without needing an account or token. Set it to `none` to perform address discovery without sending enrichment lookups to IPConfig.io.

The response supplies the country, country code, AS number, and AS organization. Region and city are included only when the address database can place the address: ordinary ISP addresses usually resolve to one, while anycast and datacenter addresses often do not. The dashboard shows those fields when they are present and leaves them empty otherwise; it never infers them. IPConfig.io reports no ISP separate from the AS organization, and no metadata field is used to claim VPN status.

Each address is looked up separately over HTTPS, results are cached with the identity snapshot, and a failed or slow lookup leaves address discovery intact. The default enrichment sends each discovered public IP address to IPConfig.io.

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

Vite serves `http://localhost:5173` and proxies `/api` to the backend. See [CONTRIBUTING.md](CONTRIBUTING.md) for the verification commands and provider safety rules. See [architecture](docs/architecture.md) for implementation boundaries and [release maintenance](docs/releasing.md) for packaging validation.
