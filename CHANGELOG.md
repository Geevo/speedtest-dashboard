# Changelog

This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and semantic versioning.

## Unreleased

### Added

- `DASHBOARD_PORT` as the simple way to change the app's listening port, including container health checks and packaged Compose files
- experimental FAST.com provider using the checksum-pinned upstream fast-cli `v0.3.5` binary on amd64 and arm64
- M-Lab provider using the checksum-pinned official `ndt7-client-go` `v0.10.1` source on amd64 and arm64, with an explicit public-data notice

### Fixed

- included the Debian interface, DHCP, route, and ping tooling required for Proxmox-managed LXC networking
- serialized authentication settings changes so concurrent preferences cannot turn off newly enabled login protection
- restored browser mutations on anonymous HTTP installations while retaining HTTPS requirements for credentials
- serialized API idempotency lookup, submission, and recording so concurrent retries return one job
- made native-template checksum files portable between the build host and the receiving Proxmox node
- required signed Debian archive verification when bootstrapping native templates

### Changed

- allowed four manual network-identity refreshes per cooldown window and made the cooldown message friendlier
- grouped Docker/Podman and native Proxmox packaging into parallel directories with shared provider tooling and an optional Ookla build guide
- excluded generated artifacts and local evaluations from version control and container build contexts
- enabled token-free IPConfig.io network identity enrichment by default; set `NetworkIdentity__MetadataProvider=none` to opt out
- deleted the local account and password when disabling login protection so re-enabling it uses fresh credentials
- moved admin account setup and password changes into focused dialogs
- moved schedule creation and editing into responsive dialogs with clearer recurrence controls
- added an expandable API reference with response models checked against backend contracts
- replaced IPinfo Lite with token-free IPConfig.io metadata covering country, AS details, and region and city where the address can be placed; select `NetworkIdentity__MetadataProvider=ipconfig` and drop `NetworkIdentity__Ipinfo__Token`, because `ipinfo` is no longer a valid provider selection
- prepared one OCI build for amd64 and arm64 with LibreSpeed included and Ookla excluded
- added independent native Proxmox LXC template tooling for amd64 and arm64
- added a version-pinned Podman builder for native Proxmox templates on non-Debian hosts
- added an opt-in, checksum-verified local native Proxmox build containing the Ookla CLI while keeping licence acceptance explicit
- added release automation with SBOM, provenance, checksums, manifest verification, and smoke tests
- replaced development-history documentation with current deployment and behavior references
- tightened responsive navigation, touch targets, overflow handling, and Overview information hierarchy

## 0.11.0 - 2026-09-05

### Added

- Results and Statistics as separate record and analytics views
- persistent one-off and recurring schedules
- instance API key and `/api/v1` routes
- optional single-operator login

### Changed

- renamed the browser History view to Results while retaining compatible History routes
