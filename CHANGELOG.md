# Changelog

This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and semantic versioning.

## Unreleased

### Changed

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
