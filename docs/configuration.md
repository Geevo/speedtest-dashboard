# Configuration

Most installs need no environment overrides. When you do want to tune something, use double underscores for nested settings - for example, `NetworkIdentity__MetadataProvider=none`.

Docker users can put the values under `environment:` in Compose. Native Proxmox LXC installs keep their overrides in `/etc/speedtest-dashboard/environment`.

`DASHBOARD_PORT` is the friendly exception to the double-underscore format. It accepts a whole number from `1` to `65535` and overrides ASP.NET's own URL and port settings when present.

## Common settings

| Variable | Default |
| --- | --- |
| `DASHBOARD_PORT` | `8080` in packaged installs |
| `Authentication__AllowInsecureHttp` | `false` |
| `Authentication__DataProtectionPath` | `/data/dataprotection` |
| `ReverseProxy__TrustForwardedHeaders` | `false` |
| `Storage__DatabasePath` | `/data/speedtest.db` |
| `Storage__CommandTimeoutSeconds` | `10` |
| `Scheduler__PollIntervalSeconds` | `30` |
| `SpeedTests__QueueCapacity` | `4` |
| `SpeedTests__QueueFullRetryAfterSeconds` | `5` |
| `SpeedTests__SseHeartbeatSeconds` | `20` |

## Process limits

| Variable | Default |
| --- | --- |
| `Processes__DefaultTimeoutSeconds` | `120` |
| `Processes__MaxTimeoutSeconds` | `600` |
| `Processes__DefaultStdoutLimitBytes` | `2097152` |
| `Processes__DefaultStderrLimitBytes` | `1048576` |
| `Processes__AbsoluteOutputLimitBytes` | `8388608` |

## Network identity

| Variable | Default |
| --- | --- |
| `NetworkIdentity__SuccessCacheSeconds` | `300` |
| `NetworkIdentity__FailureCacheSeconds` | `30` |
| `NetworkIdentity__RefreshThrottleSeconds` | `10` |
| `NetworkIdentity__RequestTimeoutSeconds` | `5` |
| `NetworkIdentity__MetadataProvider` | `ipconfig` |

The Overview page allows four manual IP refreshes during each `NetworkIdentity__RefreshThrottleSeconds` window before asking you to cool off.

`NetworkIdentity__MetadataProvider=ipconfig` annotates each discovered public address through [IPConfig.io](https://ipconfig.io). Set it to `none` to keep address discovery but skip enrichment.

Each IPv4 and IPv6 address is looked up separately over HTTPS. A slow or failed metadata request leaves public-IP discovery intact. IPConfig.io may return country, AS number, AS organisation, region, and city. The dashboard only displays fields actually returned; it does not infer a location or claim that a VPN is active.

Older installs that used the removed IPinfo Lite provider should change `NetworkIdentity__MetadataProvider=ipinfo` to `ipconfig` and remove `NetworkIdentity__Ipinfo__Token`. The old value now fails startup validation.

## LibreSpeed

| Variable | Default |
| --- | --- |
| `Providers__LibreSpeed__Enabled` | `true` |
| `Providers__LibreSpeed__ExecutablePath` | `/usr/local/bin/librespeed-cli` (container) |
| `Providers__LibreSpeed__HealthTimeoutSeconds` | `5` |
| `Providers__LibreSpeed__HealthCacheSeconds` | `45` |
| `Providers__LibreSpeed__TestTimeoutSeconds` | `180` |
| `Providers__LibreSpeed__ServerListTimeoutSeconds` | `20` |
| `Providers__LibreSpeed__ServerCacheSeconds` | `300` |
| `Providers__LibreSpeed__MaximumServers` | `250` |
| `Providers__LibreSpeed__DisableIcmp` | `true` |
| `Providers__LibreSpeed__PreferHttps` | `true` |

## FAST.com

| Variable | Default |
| --- | --- |
| `Providers__FastCom__Enabled` | `true` |
| `Providers__FastCom__ExecutablePath` | `/usr/local/bin/fast-cli` (container) |
| `Providers__FastCom__HealthTimeoutSeconds` | `5` |
| `Providers__FastCom__HealthCacheSeconds` | `45` |
| `Providers__FastCom__TestTimeoutSeconds` | `90` |
| `Providers__FastCom__DurationSeconds` | `30` |

`DurationSeconds` accepts the upstream effective range of 7–30 seconds. Download
and upload are separate phases, so `TestTimeoutSeconds` must be at least twice
the duration plus ten seconds.

## Ookla

| Variable | Default |
| --- | --- |
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

The public images and LXC templates do not contain the Ookla CLI. See [Enable Ookla](ookla.md) before changing the acceptance settings.

## Authentication

Login protection starts off. Settings can create one local operator account and turn protection on or off. Turning it off deletes that account and password, so enabling it later means choosing fresh credentials. There is no registration, account list, email recovery, or role system.

Usernames are 3–64 characters using letters, digits, `.`, `_`, or `-`. Passwords are 6–128 characters. Five failed logins lock the account for 15 minutes; login requests are also limited to 10 per minute per source IP.

Sessions use an `HttpOnly`, `SameSite=Lax` cookie with a 12-hour sliding lifetime. Cookies are Secure unless `Authentication__AllowInsecureHttp=true`. Keep that override for trusted HTTP-only networks; prefer HTTPS anywhere else.

The bearer key generated in Settings is separate from the browser login. A browser session does not authenticate `/api/v1`, and an API key does not sign into the dashboard.

## Reverse proxies

When `ReverseProxy__TrustForwardedHeaders=true`, the app clears ASP.NET Core's known-proxy restrictions. Turn it on only when untrusted clients cannot reach the application port directly and your proxy replaces forwarded headers.

## Proxmox paths

The native LXC template overrides the container-oriented defaults:

- durable data: `/var/lib/speedtest-dashboard`
- configuration: `/etc/speedtest-dashboard`
- application: `/opt/speedtest-dashboard`
- IP metadata: `NetworkIdentity__MetadataProvider=none` (opt in to `ipconfig` if wanted)

Configuration validation rejects unsafe paths and out-of-range values during startup.

[Back to the main README](../README.md)
