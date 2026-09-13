# Architecture

This document records the current boundaries that are easy to miss during maintenance. Code, migrations, and automated tests are authoritative.

## Repository structure

- `SpeedtestDashboard.Core`: test, provider, schedule, network, History, and statistics contracts
- `SpeedtestDashboard.Infrastructure`: provider adapters, process execution, queue workers, network lookups, SQLite, and schedules
- `SpeedtestDashboard.Api`: HTTP endpoints, authentication, API-key handling, and SPA hosting
- `SpeedtestDashboard.Web`: React dashboard
- `packaging/containers`: Docker/Podman image and Compose configurations
- `packaging/proxmox`: native Debian LXC rootfs build
- `packaging/providers`: shared pinned provider build/download scripts

Provider adapters, `ProcessRunner`, queue admission, persistence, authentication, statistics, and recurrence calculation remain separate because each has a security, concurrency, or test boundary. There is no second execution path for scheduled or API-created tests.

## Test execution

All requests become a `SpeedTestRequest` containing a registered provider ID and optional provider-owned server ID. Provider validation happens before the job enters the bounded in-memory queue and again before execution. The HTTP surface cannot supply commands, executable paths, URLs, environment variables, or extra CLI arguments.

One `SpeedTestWorker` reads the queue. It captures backend Network Identity, invokes one provider at a time, normalizes the result, and writes terminal job/result state transactionally. Current-job subscriptions are process-local. Terminal state is durable and can be read after restart.

`ProcessRunner` uses `ProcessStartInfo.ArgumentList` with shell execution disabled. It drains stdout and stderr concurrently, enforces separate and total output limits, applies a bounded timeout, and terminates the process tree on timeout or cancellation.

## Providers

All providers remain registered when disabled or unavailable so the UI can report their state without affecting application health.

`ProviderId` is an open validated identifier rather than an enumeration of built-in providers. Each adapter owns its descriptor, capabilities, display order, disclosures, failure codes, configuration validation, dependency registration, commands, and parsing. The registry only validates uniqueness, orders descriptors, and resolves providers; it contains no provider-specific selection policy.

The built-in provider list is explicit in one provider composition module. Adding or removing a provider may change that list, its packaging and configuration, and provider-specific tests or documentation. It must not require changes to Core, orchestration, persistence, statistics, generic API endpoints, or generic web navigation and filters. Provider-specific operational failures cross the shared boundary as opaque safe codes; Core owns only application-wide failure codes.

Server discovery is an optional provider interface. Capability metadata drives the HTTP and browser surfaces, while the interface prevents providers without discovery from implementing meaningless no-op methods. The browser obtains provider names, ordering, capabilities, guidance, and disclosures from `/api/providers` and uses generic provider routes.

LibreSpeed CLI `1.0.13` is built from commit `2f2408764d88e9601aa64a03b340f8e3151003e4`. Its JSON throughput values are already Mbps. Default execution uses HTTPS and HTTP-based ping (`--secure --no-icmp`). The application passes no sharing or telemetry flags.

The experimental FAST.com adapter consumes the upstream fast-cli `0.3.5` release unchanged. Its amd64 and arm64 archives are checksum-pinned, and normal execution uses `--https --upload --json --duration 30`. FAST.com owns target selection. The adapter treats a non-null JSON `error` as failure even though this upstream version exits successfully after emitting it. Reported throughput values are deliberately not transformed during this viability pass; results outside the CLI's Mbps display range require validation before the provider is considered production-ready.

The M-Lab adapter builds the official `m-lab/ndt7-client-go` `0.10.1` source at pinned commit `4a5f6325d1d586ab38afb84566a5781b5d6c3d9a`. Normal execution uses quiet JSON, TLS, automatic M-Lab discovery, and an inner 55-second client timeout. Download and upload throughput are consumed in `Mbit/s`; download minimum RTT is used for latency with upload minimum RTT as a fallback. NDT7 retransmission is retained as provider metadata rather than mislabelled as packet loss. M-Lab publishes measurement data, including client IP and test time, so the provider page and documentation disclose that behavior.

Ookla CLI package `1.2.0.84-1.ea6b6773cf` is optional. Public artifacts do not include it. Local OCI builds can install the checksum-pinned package with `INSTALL_OOKLA=true`; runtime commands remain unavailable until both acceptance settings are true. Ookla bytes-per-second values are converted once using `Mbps = bytesPerSecond / 125000`.

## Persistence

SQLite is the only database. OCI uses `/data/speedtest.db`; native LXC uses `/var/lib/speedtest-dashboard/speedtest.db`. EF Core applies committed migrations before serving HTTP.

Startup attempts WAL and continues with a warning if the filesystem cannot support it. A terminal job update and its result insert share a transaction. Startup reconciliation marks interrupted jobs as `failed/application_restarted`; it never resumes or re-enqueues them.

History pages use descending completion time and result ID with opaque keyset cursors. `/results` is the browser record view, while `/api/history` remains the compatible backend route family.

## Authentication and API keys

A fresh database uses anonymous dashboard access. Settings can create one ASP.NET Core Identity account and enable login protection. Cookie sessions are Secure by default, and unsafe authenticated browser requests require antiforgery validation.

The machine API accepts one instance-wide bearer key. The credential is generated from 256 bits of randomness and protected with the same durable Data Protection key ring as browser sessions. It is independent of cookie authentication. Successful use updates `LastUsedAtUtc` at most once per minute.

`POST /api/v1/tests` supports caller-provided idempotency keys up to 128 characters. Records expire after 24 hours. A key reused with different request data returns a conflict.

## Scheduling

One-off, interval, daily, and weekly schedules are stored with explicit IANA time zones. Daily and weekly recurrence resolves invalid or ambiguous local times deterministically through `TimeZoneInfo`.

The schedule worker atomically claims an occurrence and advances its next run before queue admission. Queue-full, previous-run-active, unavailable-provider, and invalid-server outcomes are recorded without retry. Startup reconciliation records one `application_offline` skip for a due schedule and advances to its next future occurrence; it does not replay missed tests.

## Statistics

Statistics are calculated from terminal History rows. Queries accept `24h`, `7d`, `30d`, `90d`, or `all`, plus an optional syntactically valid provider ID, including a removed provider retained in history. Provider comparisons derive their provider set from stored rows rather than a built-in provider list. The service returns test counts, success rate, latest/average/median/minimum/maximum values, P95 latency and jitter, prior-period trends, provider comparison, and bounded chart buckets.

Results owns individual records. Statistics owns aggregate charts and comparisons. Overview uses only Network Identity, the latest successful result, a short 7-day summary, next scheduled run, and provider availability.

## Release artifacts

The OCI job builds one `linux/amd64` and `linux/arm64` manifest from `packaging/containers/Dockerfile`. The normal image contains the application, .NET runtime, curl health check, LibreSpeed CLI, fast-cli, and the official M-Lab NDT7 client. It excludes the .NET SDK, Node, Go, source, test fixtures, caches, and Ookla CLI. BuildKit publishes SBOM and provenance attestations.

The Proxmox job independently bootstraps Debian 12 with debootstrap, publishes self-contained `linux-x64` or `linux-arm64` application files, packages LibreSpeed, fast-cli, and the M-Lab NDT7 client, installs a systemd service, and creates a rootfs `tar.zst`. It never consumes OCI output. Native state lives under `/var/lib/speedtest-dashboard`.

Both jobs take version and revision from the same semantic-version tag and source commit. A release is incomplete until the OCI manifest, native artifact checksums, and release assets are verified. Native Proxmox support also requires a real unprivileged-container test; inspecting or unpacking the archive is not enough.
