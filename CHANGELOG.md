# Changelog

This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and semantic versioning.

## Unreleased

### Fixed

- serialized API idempotency lookup, submission, and recording so concurrent retries return one job
- restored browser mutations on anonymous HTTP installations while retaining HTTPS requirements for credentials
- serialized authentication settings changes so concurrent preferences cannot turn off newly enabled login protection

### Changed

- moved schedule creation and editing into responsive dialogs with clearer recurrence controls
- moved admin account setup and password changes into focused dialogs
- enabled token-free IPConfig.io network identity enrichment by default; set `NetworkIdentity__MetadataProvider=none` to opt out
- deleted the local account and password when disabling login protection so re-enabling it uses fresh credentials
- replaced IPinfo Lite with token-free IPConfig.io metadata covering country, AS details, and region and city where the address can be placed; select `NetworkIdentity__MetadataProvider=ipconfig` and drop `NetworkIdentity__Ipinfo__Token`, because `ipinfo` is no longer a valid provider selection
- prepared one OCI build for amd64 and arm64 with LibreSpeed included and Ookla excluded
- added independent native Proxmox LXC template tooling for amd64 and arm64
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
