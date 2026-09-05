# Speedtest Dashboard

Speedtest Dashboard is a self-hosted network utility for measuring the connection of the machine or container running its backend. Public-IP lookups and speed tests originate from the ASP.NET Core service - not from the browser - so the dashboard reports the egress path that matters for a server, VPN gateway, or homelab workload.

> [!IMPORTANT]
> Version 0.11 starts with anonymous dashboard access. A homelab operator can optionally enable the single local login from Settings; credential entry requires HTTPS unless insecure HTTP is explicitly enabled for a deliberately trusted LAN. A separate, optional instance-wide API key authenticates machine clients against `/api/v1`, and the Schedules page runs one-off or recurring tests automatically. An observed IP address identifies egress; it is not proof that a VPN is active.

The project is a new implementation inspired by the useful user-facing flows in [`moranbw/speedtest-app`](https://github.com/moranbw/speedtest-app): remote-machine testing, optional server selection, results, and straightforward container deployment. It does not copy that project's Node/Material UI architecture.

## Milestone status

Milestones 1 through 11 are implemented:

- .NET 10 solution with API, Core, and Infrastructure projects
- React 19, Vite, and TypeScript frontend
- Tailwind CSS 4, shadcn-style components, and Lucide icons
- TanStack Query API integration, a record-focused Results view, and bounded Recharts analytics on Statistics
- responsive dashboard shell with collapsible desktop navigation and a mobile drawer
- `GET /api/health`, RFC 7807 errors for unknown API routes, forwarded-header support, and baseline response/request hardening
- production SPA static hosting from ASP.NET Core
- non-root, multi-stage OCI image with a persistent `/data` volume and health check
- API integration tests and frontend type/lint checks
- independent backend-origin IPv4 and IPv6 discovery through ipify
- optional, per-address IPinfo Lite country and ASN enrichment
- bounded success/failure caching, refresh-storm prevention, and server-enforced manual-refresh throttling
- a responsive Network Identity panel with partial, unavailable, stale, and mixed-egress states
- provider-neutral contracts, capability reporting, health checks, and an Ookla production provider that remains visible when unavailable
- an in-memory test lifecycle with a bounded queue, one active bandwidth test, cancellation, and SSE status updates
- a shell-free process runner with literal argument lists, bounded output, timeouts, cancellation, and process-tree termination
- test-only fake providers and a controlled child executable covering orchestration and process safety without running Internet speed tests
- pinned, checksum-verified optional Ookla CLI packaging for `linux/amd64` and `linux/arm64`
- cached Ookla health and nearby-server discovery, automatic or validated explicit selection, and normalized machine-readable results
- a responsive Ookla workflow with searchable server selection, honest active stages, cancellation, and complete result presentation
- EF Core 10 SQLite migrations, durable terminal jobs/results, WAL initialization, and restart reconciliation
- cursor-paginated History list/detail/delete APIs with provider, terminal-status, and UTC range filters
- persisted Overview latest/recent data and a responsive Results page with filters, pagination, and inline expandable result detail
- neutral light, dark, and system-default themes with a locally persisted explicit preference
- pinned LibreSpeed CLI source packaging, structured public-server discovery, automatic/explicit selection, normalized Mbps results, and the shared provider/History workflow
- roadmap cleanup that keeps the generic provider architecture while limiting the current product surface to implemented integrations
- one local operator account backed by ASP.NET Core Identity, a secure HttpOnly cookie session, login lockout, and no public registration
- fallback authorization for dashboard APIs, antiforgery validation for unsafe cookie-authenticated requests, and an anonymous health endpoint
- first-run anonymous access, Settings-managed optional login, durable Data Protection keys below `/data`, password change, logout, and a hideable anonymous-access notice
- one optional instance-wide API key, protected at rest with Data Protection and viewable/copyable/regenerable/revocable from Settings, authenticating machine clients with `Authorization: Bearer` against a stable `/api/v1` surface
- `/api/v1` Network Identity, provider inspection, test creation/status/cancellation, and History reads that reuse the existing bounded queue, job store, and History store; caller-supplied `Idempotency-Key` support for test creation; and read/write rate limiting independent of the queue's own bounded-capacity `429`
- persistent one-off and recurring (interval/daily/weekly) speed-test schedules with explicit IANA time zones, daylight-saving-correct recurrence math, and a Schedules dashboard page
- one `ScheduleWorker` background service that submits due schedules through the same queue-admission path as manual and machine test creation, with skip-not-retry handling for a full queue, an already-active previous run, or a since-invalidated provider/server, and no replay of occurrences missed while the application was offline
- History-derived Statistics with summary metrics, P95 latency/jitter, previous-period trends, provider comparison, and bounded throughput/latency charts

Ookla and LibreSpeed are the two production speed-test providers. Both stay registered when their optional CLIs are absent. Ookla license acceptance is disabled by default; LibreSpeed requires no application-level acceptance flag. Terminal history is durable when `/data` is persisted, while the bounded execution queue remains in memory.

## Architecture

The repository intentionally uses three backend projects rather than a large ceremonial Clean Architecture graph:

- `SpeedtestDashboard.Core`: provider-neutral models, contracts, and validation
- `SpeedtestDashboard.Infrastructure`: process execution, provider adapters, network identity, EF Core migrations, and SQLite persistence
- `SpeedtestDashboard.Api`: HTTP endpoints, background orchestration, and production SPA hosting
- `SpeedtestDashboard.Web`: React dashboard and provider workflows

See [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) for the proposed contracts, database model, process-safety design, and milestone acceptance criteria.

## Authentication

Speedtest Dashboard is a single-operator homelab application. A fresh instance opens anonymously so it works immediately without filesystem secrets or mandatory account setup. Settings initially offers a **Set up login** action instead of showing an unavailable switch. Creating the one local operator account enables login protection and signs the current browser in. Once that account exists, a **Login protection** switch controls whether the instance requires it; turning protection off requires the current password and returns the instance to anonymous access, while turning it back on verifies the existing credentials.

There are no roles, invitations, user-management pages, email flows, or public registration. Browser authentication uses an ASP.NET Core Identity cookie - not a JWT in browser storage. When login protection is on, dashboard APIs require the local session except `GET /api/health`, `GET /api/auth/session`, `GET /api/auth/csrf`, and `POST /api/auth/login`.

The Settings form accepts a 3–64 character username made from letters, digits, dots, underscores, or hyphens. Passwords must be 6–128 characters with no control characters. Digits, uppercase, lowercase, and punctuation are not individually required. Credentials use ASP.NET Core Identity hashing and are stored only in the SQLite database under `/data`.

Failed login attempts lock the account for 15 minutes after five failures. A separate source-IP limiter allows ten login requests per minute. Sessions are non-persistent browser cookies with a 12-hour sliding lifetime, `HttpOnly`, `SameSite=Lax`, and `Secure` by default. The operator can change the password under Settings; the current browser remains signed in while the Identity security stamp invalidates other cookies within two minutes. Sign out is a CSRF-protected POST.

Cookie-authenticated `POST`, `PUT`, `PATCH`, and `DELETE` operations require the `X-CSRF-TOKEN` antiforgery header. The SPA obtains the framework-issued token from `/api/auth/csrf`, keeps it only in memory, and refreshes it after login or password change. Auth/session responses use `Cache-Control: no-store`. API authentication failures are 401/403 responses, never HTML redirects, and same-origin SSE uses the cookie without credentials in its URL.

Production Local mode rejects login over HTTP and emits Secure cookies. Terminate TLS at a reverse proxy and configure forwarded headers only for a trusted proxy path. For direct use on an isolated, deliberately trusted homelab network, `Authentication__AllowInsecureHttp=true` permits HTTP and non-Secure cookies; the deployment is then responsible for preventing credential interception.

While login protection is off, ordinary dashboard writes do not require antiforgery because there is no ambient authentication credential. The Settings operations that enable login or change the notice preference are still antiforgery-protected. The dashboard displays an anonymous-access notice by default; **Show anonymous-access notice** in Settings can hide or restore it for the whole instance.

Human browser sessions and machine access remain separate:

```text
Human dashboard: secure cookie session
Machine API:     one instance-wide API key (see "Machine API and /api/v1" below)
```

For password-loss recovery, stop the application and back up `/data`. Restore a known-good backup, or use an offline SQLite administration tool to set `DashboardSettings.AuthenticationEnabled` to `0`. Restart, open Settings anonymously, and verify the existing credentials to turn login back on. History is not user-owned and remains intact. Never edit the live database or remove only password-hash fields.

In the user interface, **Results** means individual test records and **Statistics** means aggregate analysis and trends. Persisted backend History feeds both views. `/results` is the canonical records route; `/history` redirects there for old bookmarks. The backend `/api/history` routes retain their established names. Additional provider or protocol integrations may be considered later.

## Machine API and /api/v1

Speedtest Dashboard supports zero or one instance-wide API key for machine clients. It belongs to the instance, not a user: there are no roles, scopes, a permission matrix, multiple keys, or a required expiration. The Settings **API access** section lets the authenticated operator generate, view, copy, regenerate, or revoke it; unlike a typical one-time secret, the full key is deliberately viewable again at any time so an operator who lost it does not need to regenerate and update every client. Generating or viewing the key follows the same authentication rule as the rest of Settings - if login protection is off, no additional password prompt is added.

The key is generated as `std_<43-character base64url secret>` from 256 bits of random entropy. It is protected at rest with ASP.NET Core Data Protection (purpose `SpeedtestDashboard.ApiCredential.v1`, using the same key ring under `/data/dataprotection` as the login cookie) and stored only in SQLite; the raw or protected value is never written to environment variables, filesystem configuration, or browser storage, and API-key responses use `Cache-Control: no-store`. The key and any bearer token presented in a request are never logged.

Machine clients authenticate with `Authorization: Bearer <api-key>` - never a query parameter or cookie - against a dedicated `ApiKey` scheme that is entirely independent of dashboard login: a signed-in dashboard cookie does not authenticate `/api/v1`, and an API key does not sign in the dashboard. A missing, malformed, wrong, or revoked key all return a uniform 401 that does not reveal which check failed. Regenerating the key immediately invalidates the previous one; revoking it removes the credential so every `/api/v1` request returns 401 until a new key is generated. `LastUsedAtUtc` updates after a successful request, throttled to at most once per minute to bound database writes. `/api/v1` requests carry no ambient browser credential, so they do not require the `X-CSRF-TOKEN` antiforgery header; cookie-authenticated dashboard writes still do. The dashboard does not enable additional CORS for `/api/v1` - machine clients do not run in a browser.

`/api/v1` is a stable, separate surface below the existing dashboard API, which is unchanged and remains what the React app calls:

| Method and route | Contract |
| --- | --- |
| `GET /api/v1/network` | The same `NetworkIdentityResponse` as the dashboard; independent IPv4/IPv6, informational only, no VPN detection or policy evaluation. |
| `GET /api/v1/providers` | Provider summaries with capabilities and health, excluding executable paths, CLI arguments, and license secrets. |
| `GET /api/v1/providers/{providerId}` | One provider summary or 404. |
| `GET /api/v1/providers/{providerId}/servers` | The same validated server-discovery contract as the dashboard. |
| `POST /api/v1/tests` | The same provider-neutral `{ providerId, serverId }` request as the dashboard, reusing the existing bounded queue and job store. Returns `202 Accepted` with a job ID and `/api/v1/tests/...` status/URL. Supports a caller-supplied `Idempotency-Key` header. |
| `GET /api/v1/tests/{id}` | Current job state, including the result once terminal. Polling only; SSE is not exposed in this milestone. |
| `POST /api/v1/tests/{id}/cancel` | Requests cancellation through the same cancellation registry as the dashboard. |
| `GET /api/v1/history` | The same cursor-paginated `provider`/`status`/`fromUtc`/`toUtc`/`cursor`/`limit` query as the dashboard History list. |
| `GET /api/v1/history/{id}` | Full normalized historical result. |
| `GET /api/v1/statistics` | History-derived test counts, metric summaries, trends, provider comparison, and bounded chart buckets for `24h`, `7d`, `30d`, `90d`, or `all`. |

History deletion and authentication/API-key administration are intentionally not exposed through `/api/v1` in this milestone.

`Idempotency-Key` is capped at 128 characters and retained for 24 hours. A repeated `POST /api/v1/tests` with the same key and the same `{ providerId, serverId }` returns the already-created job instead of enqueuing a second one. The same key with different request data returns `409 Conflict` rather than silently using either payload. This is a small, bounded convenience - not a general distributed-idempotency system - so an expired key is treated as unused and a fresh request under it creates a new job.

`/api/v1` applies its own rate limiter, separate from and in addition to the existing bounded test queue: generous limits on reads, stricter limits on test creation. A limiter rejection returns `429` with `Retry-After`, the same as a full queue's `429`, but the two are independent - raising the rate limit does not raise the number of concurrent bandwidth tests, and a request that clears the rate limiter can still be rejected by a full queue.

## Speed-test execution engine

`POST /api/tests` accepts only a validated provider ID and an optional provider-owned server ID. It cannot accept executable paths, server URLs, command strings, or arbitrary CLI flags. Each provider owns its executable, fixed arguments, and request validation. Ookla IDs are limited to 1–10 decimal digits; LibreSpeed IDs must be positive 32-bit integers without leading zeroes. The validated-string provider identity and provider-side validation boundary remain extensible for future typed integrations.

Accepted jobs use an in-memory lifecycle (`queued`, `starting`, `running`, `processingResult`, then a terminal state). Job state transitions are also persisted, but the queue remains bounded and process-local. It defaults to four waiting jobs and rejects excess admission with HTTP 429 and `Retry-After`; a rejected admission is removed rather than appearing in History. One background reader invokes at most one provider at a time so concurrent bandwidth tests cannot contaminate each other. Queued or running jobs can be cancelled, and a running provider receives a cancellation token.

`GET /api/tests/{id}` returns the in-memory snapshot for current work and falls back to persisted terminal state after a restart. `GET /api/tests/{id}/events` sends an immediate snapshot followed by typed, monotonically versioned SSE state/result/error events and heartbeat events for process-local jobs. The frontend helper falls back to five-second polling only after repeated stream failures. Old SSE streams are not replayed. On startup, persisted non-terminal work is failed once with `application_restarted`; it is never re-enqueued or allowed to consume bandwidth automatically.

The process runner uses `ProcessStartInfo.ArgumentList` with `UseShellExecute=false`, concurrently drains stdout and stderr, enforces separate and absolute output limits, distinguishes timeout/cancellation/output overflow/start failure, and terminates the child process tree when required. Shell executables are rejected. Raw process output is never returned through the job or SSE APIs.

## Scheduling

The Schedules page manages persistent one-off and recurring speed tests. A schedule picks a provider and optional server exactly like a manual test - the same request validation applies - and stores an explicit IANA time zone (for example `Europe/London` or `America/New_York`) rather than assuming the browser's or server's local zone. `NextRunAtUtc` and `LastRunAtUtc` are always UTC; the dashboard renders them in the schedule's own time zone.

Recurrence is one of four kinds: **one-off** at a chosen date and time; **interval**, every 1–10080 minutes; **daily**, at a time of day; or **weekly**, on a chosen day and time of day. There is no general cron expression support. A one-off schedule's next-run date is computed once and cleared when its due occurrence is durably claimed, so it never runs twice. Interval schedules stay anchored to their original cadence even when a poll is late. Daily and weekly recurrence convert through `TimeZoneInfo`: a time that falls in a spring-forward gap resolves minute-by-minute to the first valid instant after it, including zones with non-hour transitions, and a time that occurs twice during a fall-back is resolved to the zone's standard-time interpretation - both deterministic, not best-effort.

One `ScheduleWorker` background service polls for due schedules (default every 30 seconds; configurable but not busy-looping) and submits each one through the exact same `ISpeedTestSubmissionService` used by manual dashboard creation and `POST /api/v1/tests`. The scheduler never calls a provider directly and never bypasses the bounded `SpeedTestQueue` or its single active bandwidth test; a scheduled job is a normal job the existing worker executes and normal History records once it completes.

Before checking a provider or submitting a job, the worker atomically claims the expected due occurrence in SQLite and advances `NextRunAtUtc`. A concurrent edit, disable, or prior worker claim makes that compare-and-swap fail before queue admission. The internal pending run is then finalized as queued, skipped, or failed; a pending claim left by an interrupted process is recovered as `application_offline` on startup. This ordering prevents a schedule-recording failure from queuing the same bandwidth test again on the next poll.

A due occurrence is skipped - recorded with a stable failure code and advanced to the next occurrence - rather than retried immediately when: the queue is full (`queue_full`); the schedule's previous job is still running (`previous_run_active`, preventing duplicate concurrent jobs from the same schedule); or the saved provider or server has since become unavailable or invalid (`provider_unavailable`, `invalid_server`). None of these delete the schedule or its configuration.

If the application was offline when one or more occurrences were due, startup reconciliation loads each affected schedule once, records a single skipped run with failure code `application_offline`, and recalculates the next future occurrence - it does not enqueue a test during reconciliation and does not replay the occurrences that were missed. An expired one-off schedule is marked missed the same way and never runs.

Disabling a schedule stops it from executing while keeping its configuration; re-enabling recalculates a future `NextRunAtUtc` rather than immediately running whatever was missed while it was off. Editing a schedule always recalculates `NextRunAtUtc`; past schedule-run history is never mutated. Deleting a schedule stops future runs but does not delete History, jobs, or its own run history.

`GET /api/schedules`, `GET /api/schedules/{id}`, `POST /api/schedules`, `PUT /api/schedules/{id}`, `DELETE /api/schedules/{id}`, and `POST /api/schedules/{id}/enable`/`disable` are dashboard endpoints under the existing cookie/CSRF rules. `GET /api/v1/schedules` and `GET /api/v1/schedules/{id}` expose the same data read-only under the API key; schedule creation and editing remain dashboard-only in this milestone. Scheduled test execution is not HTTP traffic and is not subject to the `/api/v1` rate limits - it is bound only by the same queue capacity and single-active-test rule as everything else.

## Ookla licensing and configuration

> [!IMPORTANT]
> Ookla Speedtest CLI is proprietary software separate from this MIT-licensed application. Ookla describes the official CLI as intended for personal, non-commercial use. Before enabling it, review Ookla's [EULA](https://www.speedtest.net/about/eula), [Terms of Use](https://www.speedtest.net/about/terms), and [Privacy Policy](https://www.speedtest.net/about/privacy). This project does not accept those terms for you and does not include an Ookla binary in source control.

The provider is registered even when the CLI is absent or acceptance is not configured. This keeps `/api/health` healthy and lets the Ookla page explain the administrative state. Functional commands are license-gated through server configuration; there is deliberately no browser acceptance button. Ookla legal acceptance remains deployment configuration. It is deliberately not stored or toggled through the dashboard.

To build a local image containing the pinned official CLI package:

```bash
podman build --format docker \
  --build-arg INSTALL_OOKLA=true \
  --tag speedtest-dashboard:milestone-4 .
```

The Dockerfile downloads version `1.2.0.84-1.ea6b6773cf` directly from Ookla's official Packagecloud repository and verifies its SHA-256 before `dpkg` installation:

- `amd64`: `35e084567a6388631fb10cf01e5e0d6b57a67d34ede2b72ba111b3d9164c8b94`
- `arm64`: `98e7de9db3bf181d08bc67e647bcfc71349c8014e387289c08e54e5c55d82f37`

The hashes are recorded on the official Packagecloud artifact pages for [amd64](https://packagecloud.io/ookla/speedtest-cli/packages/debian/bookworm/speedtest_1.2.0.84-1.ea6b6773cf_amd64.deb?distro_version_id=215) and [arm64](https://packagecloud.io/ookla/speedtest-cli/packages/debian/bookworm/speedtest_1.2.0.84-1.ea6b6773cf_arm64.deb?distro_version_id=215), and were independently checked against the downloaded packages. The repository bootstrap script is not used. `TARGETARCH` is mapped explicitly and an unsupported architecture fails an Ookla-enabled build.

After reviewing and accepting the official terms, the administrator must explicitly configure both acceptance values:

```yaml
environment:
  Providers__Ookla__Enabled: "true"
  Providers__Ookla__AcceptLicense: "true"
  Providers__Ookla__AcceptGdpr: "true"
```

Only then does the provider add `--accept-license` and `--accept-gdpr` as separate process arguments for discovery and tests. Defaults remain `true`, `false`, and `false`, respectively. Other bounded options are documented in `docker-compose.yml`: the health cache is 45 seconds, nearby servers are cached for 300 seconds, discovery times out after 30 seconds, and a complete test after 180 seconds.

Server discovery uses the pinned CLI's `--servers` table output and a dedicated strict parser. Search is performed over the cached normalized list, so typing does not launch another CLI process. Automatic selection passes no selector; an explicit selection adds exactly one `--server-id=<validated-id>` argument.

Complete tests use `--format=json --progress=no`. Ookla machine-readable bandwidth is bytes per second and is converted exactly once in the backend using `Mbps = bytesPerSecond / 125000`. Idle ping supplies the common latency and jitter values; unavailable packet loss remains `null`. Result links are exposed only when they are safe HTTPS `speedtest.net/result/...` URLs. A bounded known metadata object retains loaded latency, byte counts, interface addresses, server connection details, and result identity; only this normalized object may be persisted. Raw CLI JSON, stdout, and stderr are never stored or returned by the API.

Running a test necessarily communicates with Ookla/Speedtest infrastructure and sends network/device-related information under Ookla's policies. The application sends results to no additional analytics or third party. Its Network Identity snapshot remains authoritative for the dashboard; an Ookla-reported external address is diagnostic metadata and is not treated as proof of a VPN leak.

Anyone publishing a prebuilt image containing the Ookla package must independently determine whether that distribution complies with Ookla's terms. Building or testing a local image does not settle redistribution rights.

## LibreSpeed provider

LibreSpeed is the second production provider and remains visible when its executable is missing or disabled. Health uses a bounded `librespeed-cli --version` probe cached for 45 seconds; missing, disabled, timed-out, malformed-version, degraded-version, and ready states are reported without affecting `/api/health`.

The image builds LibreSpeed CLI `v1.0.13` from exact upstream commit `2f2408764d88e9601aa64a03b340f8e3151003e4` in a pinned Go `1.27.1` stage. The source archive SHA-256 is `5e6622697bf5b651d552458997856dcd7f86e07e4bd44508aaad1be238085b11`. Its checksum is verified before extraction, Go modules remain locked by upstream `go.sum`, and unsupported target architectures fail explicitly. Enable source packaging with:

```bash
podman build --format docker \
  --build-arg INSTALL_OOKLA=true \
  --build-arg INSTALL_LIBRESPEED=true \
  --tag speedtest-dashboard:milestone-7 .
```

`amd64` and `arm64` map explicitly to Go's corresponding target architectures with `CGO_ENABLED=0`, producing a static Linux binary. Go and source files are not copied into the runtime image. LibreSpeed CLI is LGPL-3.0 licensed; its license is installed at `/usr/share/licenses/librespeed-cli/LICENSE`, and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) records attribution and the exact source version.

Server discovery retrieves the canonical structured catalogue from `https://librespeed.org/backend-servers/servers.php` through a provider-owned bounded HTTPS client. The URL is not browser-configurable. Responses are limited to 2 MiB, parsed strictly, capped at 250 entries, and cached for 300 seconds. The dashboard's server dropdown searches the normalized cached list by numeric ID, name/location, sponsor, and host. Automatic selection passes no server selector; explicit selection adds `--server` and one validated ID as separate arguments.

Normal execution is `librespeed-cli --json --no-icmp --secure`, plus the optional validated server selector. `--no-icmp` uses HTTP ping/jitter and requires no `NET_RAW` capability. Certificate verification remains enabled. The application never adds `--share`, telemetry options, custom catalogue arguments, interface/source bindings, firewall marks, or certificate bypasses.

Pinned source inspection and fixtures confirm that JSON `download` and `upload` are already Mbps, so they are mapped directly without Ookla's bytes-per-second conversion. `ping` supplies common latency and `jitter` supplies common jitter. Packet loss and result URL remain `null`. Known bounded metadata retains bytes sent/received, provider-reported client IP/organization, actual server URL, upstream timestamp, and the fact that HTTP ping was used. Network Identity remains the authoritative egress snapshot, and raw JSON/stdout/stderr is never persisted.

Running a LibreSpeed test contacts the selected public backend and consumes bandwidth. The provider does not send telemetry or share results by default, but the backend necessarily sees network/device request information required to serve the test. Automated verification uses fixtures and does not run a public bandwidth test.

## Local development

Prerequisites: .NET SDK 10, Node.js 22 or newer, and npm.

Create a writable development data directory and run the API with an explicit local storage path:

```bash
mkdir -p .data
Storage__DatabasePath="$PWD/.data/speedtest.db" \
Authentication__DataProtectionPath="$PWD/.data/dataprotection" \
Authentication__AllowInsecureHttp=true \
  dotnet run --project src/SpeedtestDashboard.Api --urls http://localhost:5080
```

In a second terminal, run Vite. Requests below `/api` are proxied to port 5080.

```bash
npm install --prefix src/SpeedtestDashboard.Web
npm run dev --prefix src/SpeedtestDashboard.Web
```

Open `http://localhost:5173`.

## Container deployment

Docker and rootless Podman are supported without the Docker socket, privileged mode, `NET_ADMIN`, or host networking.

```bash
docker compose up --build -d
```

Or with Podman:

```bash
podman build --format docker -t speedtest-dashboard:local .
podman run --detach --name speedtest-dashboard \
  --publish 8080:8080 \
  --volume speedtest-data:/data:Z \
  --env Authentication__AllowInsecureHttp=true \
  --read-only \
  --tmpfs /tmp:rw,size=64m,mode=1777 \
  --security-opt no-new-privileges \
  speedtest-dashboard:local
```

Open the dashboard anonymously, then use Settings if you want to require a login. Put the service behind HTTPS before entering credentials. For direct HTTP on a deliberately trusted LAN, the example explicitly sets `Authentication__AllowInsecureHttp=true`; remove it when an HTTPS reverse proxy supplies the effective request scheme.

The container image is disposable and `/data` is the durability boundary. Retain a named volume or bind mount at `/data` across replacement. It contains `speedtest.db`, possible `speedtest.db-wal`/`speedtest.db-shm` files, and `/data/dataprotection/*`. The latter preserves login cookies and the protected API key across container replacement. SQLite contains History, authentication state (the Identity password hash), the protected API credential, and schedules with their run history, while the Data Protection key ring contains sensitive cryptographic material and is not claimed to be encrypted at rest on Linux. Protect volume permissions and backups accordingly. Replacing the container without retaining `/data` loses History, authentication state, any generated API key, and all schedules; that is an accepted consequence of treating the container as disposable, not a bug.

### Upgrading from 0.7.x, 0.8.x, or 0.9.x

Before the first 0.10.x start:

1. Back up and retain the existing `/data` volume.
2. Start 0.10.x so the forward migrations preserve History, authentication settings, and the API credential, and add the (initially empty) schedule and schedule-run tables.
3. Open the dashboard; it continues with anonymous access, no API key, and no schedules after upgrade.
4. Optionally open Settings to turn on Login protection and/or generate an API key, then create schedules from the new Schedules page.
5. Never remove `/data`; it contains History, account state, preferences, the API credential, schedules, and Data Protection keys.

## SQLite persistence and History

EF Core 10 applies committed migrations before the HTTP server begins accepting requests. Required storage initialization failure stops startup clearly. The server constructs its own SQLite connection using `Data Source=/data/speedtest.db`, `Mode=ReadWriteCreate`, `Foreign Keys=True`, and a finite default/command timeout of 10 seconds. Override `Storage__DatabasePath` or `Storage__CommandTimeoutSeconds` only through administrator configuration; neither is accepted from the browser.

Startup attempts `PRAGMA journal_mode=WAL` once and verifies the returned mode. If the mounted filesystem cannot provide WAL, the application logs a warning and continues with SQLite's available journal mode. With WAL, `speedtest.db-wal` and `speedtest.db-shm` may appear beside the database under `/data`; no additional writable root directory is required.

Completed, failed, and cancelled jobs are written as terminal History rows. A terminal job update and its unique result row share one database transaction. Normalized measurements use SQLite `REAL`/application `double`; domain and API values remain provider-neutral decimal values where already established. Database timestamps are UTC `DateTime`, while APIs continue returning UTC `DateTimeOffset`. IPv4 and IPv6 egress snapshots retain separate addresses, ASN/AS names, location, ISP, and source columns. Provider metadata is limited to 32 KiB; oversized data fails finalization with the sanitized `persistence_failed` outcome instead of being truncated or stored unbounded.

`GET /api/history` returns at most 50 terminal records by default and 200 when explicitly requested. Provider, terminal status, and UTC `fromUtc`/`toUtc` filters use descending `CompletedAtUtc`, then descending numeric result ID. The opaque cursor contains only those continuation values. `GET /api/history/{id}` returns normalized detail, and `DELETE /api/history/{id}` deletes that result and its terminal job after UI confirmation. There is no automatic retention or bulk delete in Milestone 5.

SQLite deployment is intentionally single-instance: do not point multiple application containers at one database file. EF Core's `__EFMigrationsLock` is never cleared automatically. If a migration process was forcibly terminated and leaves an abandoned lock, first ensure no other dashboard or migration process is using the database, back up `/data`, then follow the current EF Core SQLite migration-lock recovery guidance or remove only the abandoned lock table with an appropriate SQLite administration tool.

Users who care about History should back up the entire `/data` volume. For a simple manual backup, stop the container before copying the database/volume so the main database and any WAL state are consistent. Online-backup tooling and automatic retention remain future operational work.

For a host bind mount, ensure the target directory is writable by container UID/GID `1654`; do not make unrelated host paths world-writable. Rootless Podman users may need an appropriate ownership mapping or a named volume.

## Network Identity

`GET /api/network` and the Overview panel report the public Internet identity observed by the ASP.NET Core backend. The browser calls only the local dashboard API; the IPv4 and IPv6 lookups originate from the running backend process or container. This is the relevant path when the dashboard shares or routes through another container, including a VPN gateway.

IPv4 and IPv6 are resolved independently through [ipify](https://www.ipify.org/). A machine may therefore report dual stack, IPv4 only, IPv6 only, or unavailable without one family suppressing the other. Every response is parsed as an IP address, checked against its expected family, and screened for non-public ranges. The default configuration sends only the requests required to discover the public addresses.

Country and Autonomous System metadata are optional. To enable [IPinfo Lite](https://ipinfo.io/developers), create a Lite token and configure the backend:

```yaml
environment:
  NetworkIdentity__MetadataProvider: "ipinfo"
  NetworkIdentity__Ipinfo__Token: "your-token"
```

The token stays server-side and is sent to IPinfo in an authorization header; it is not included in dashboard responses, frontend assets, request URLs, or application messages. With no token - or with the default `MetadataProvider=none` - address discovery continues normally and metadata remains absent. IPinfo Lite supplies country and ASN data, not ISP, region, city, or VPN status, so the dashboard does not infer those fields. Public uses of IPinfo Lite must provide attribution; the panel displays attribution whenever IPinfo data is present.

Successful or partially useful results are cached for 300 seconds; unavailable and stale results for 30 seconds. Concurrent misses share one lookup, and accepted manual refreshes bypass the cache but are limited to one every 10 seconds. A throttled `GET /api/network?refresh=true` returns RFC 7807 Problem Details with HTTP 429 and `Retry-After`. These timings, plus the five-second external request timeout, are configurable through the commented `NetworkIdentity__...` values in `docker-compose.yml`.

Public-address discovery necessarily makes outbound HTTPS requests from the container to ipify. Enabling enrichment also sends each discovered address to IPinfo. An observed address identifies egress; it does not prove that a VPN is connected. Different IPv4 and IPv6 ASN or country metadata is shown as “mixed egress,” which may indicate split routing but is not automatically classified as a VPN leak.

The application layers build for both `linux/amd64` and `linux/arm64` using the corresponding official Node and .NET base images. The optional Ookla layer maps those architectures to separately pinned packages; the LibreSpeed build maps them to static Go targets. A multi-platform release can be produced only after the publisher completes the separate Ookla redistribution review:

```bash
docker buildx build --platform linux/amd64,linux/arm64 -t example/speedtest-dashboard:latest --push .
```

`INSTALL_OOKLA=false` and `INSTALL_LIBRESPEED=false` are Dockerfile defaults. An image without either binary still starts, shows both registered providers, and reports each missing CLI without degrading application health. The compose example enables the redistributable LibreSpeed source build and leaves Ookla disabled. Controlled child-process fixtures exist only in tests and are not copied into the runtime image.

## Reverse proxies

Forwarded headers are processed before other middleware. ASP.NET Core trusts only its default known proxies unless explicitly configured. When - and only when - the container is isolated behind a trusted reverse proxy, enable forwarded headers from the proxy network:

```yaml
environment:
  ReverseProxy__TrustForwardedHeaders: "true"
```

Do not enable this setting when untrusted clients can connect directly to port 8080 because they could spoof forwarded headers. Have nginx, Caddy, or Traefik replace (not append untrusted input to) `X-Forwarded-For`, `X-Forwarded-Host`, and `X-Forwarded-Proto`.

Path-base hosting remains a later compatibility enhancement; hosting at `/` is the supported configuration.

## Proxmox

Run the OCI image under Docker or Podman inside a Proxmox VM or a suitably configured LXC guest. Native Proxmox LXC packaging is outside the initial scope. Rootless Podman inside a VM is the simplest least-privilege option.

## Verification

```bash
dotnet format SpeedtestDashboard.sln --verify-no-changes --no-restore
dotnet restore SpeedtestDashboard.sln --locked-mode -p:AuditPipeline=true
dotnet build SpeedtestDashboard.sln --configuration Release --no-restore
dotnet test SpeedtestDashboard.sln --configuration Release --no-build
dotnet ef migrations has-pending-model-changes \
  --project src/SpeedtestDashboard.Infrastructure \
  --startup-project src/SpeedtestDashboard.Api
npm ci --prefix src/SpeedtestDashboard.Web
npm run verify:dependencies --prefix src/SpeedtestDashboard.Web
npm audit --prefix src/SpeedtestDashboard.Web
npm run check --prefix src/SpeedtestDashboard.Web
npm run build --prefix src/SpeedtestDashboard.Web
podman build --format docker --build-arg INSTALL_OOKLA=true --build-arg INSTALL_LIBRESPEED=true -t speedtest-dashboard:milestone-11 .
```

Use `podman build --format docker` for the final command when Docker is unavailable. Podman's default OCI image format does not store Docker-compatible image health-check metadata; the Docker format preserves it while remaining runnable by Podman.

## Dependency safety policy

Non-Microsoft package updates have a 14-day release-age cooldown. Direct npm versions and all resolved dependency graphs are locked; `npm run verify:dependencies` rejects any resolved npm or non-Microsoft NuGet package younger than 14 days and runs the npm vulnerability audit. Renovate uses the same strict, timestamp-required cooldown before proposing updates.

First-party `Microsoft.*`, `System.*`, and `NETStandard.Library` NuGet packages are exempt from the age delay so security and platform servicing releases can be adopted immediately. NuGet restore audits direct and transitive packages at `low` severity or higher; verification treats audit warnings and unavailable audit data as errors.

The age check is a supply-chain dwell-time safeguard, not a substitute for vulnerability scanning. A known vulnerable package must still be rejected regardless of age.

## Licensing

The application source is available under the [MIT License](LICENSE). Provider tools retain their own licenses:

- Ookla Speedtest CLI is proprietary, described by Ookla as for personal, non-commercial use, and governed by its separate EULA, Terms of Use, and Privacy Policy. It is not committed to this repository; build-time inclusion and runtime acceptance are both explicit.
- LibreSpeed CLI is LGPL-3.0 licensed.

Container redistribution must preserve all required third-party notices and comply with Ookla's terms before the Ookla binary is included in a published image.
