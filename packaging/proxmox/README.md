# Native Proxmox LXC appliance

This tool builds a Debian 12 (`bookworm`) root filesystem for Proxmox VE. The application runs directly under systemd as the unprivileged `speedtest` user. Docker and Podman are not used during the build or at runtime.

## Build

Install .NET SDK 10.0.400, Node.js 22.23.2, npm, Go 1.27.1, `debootstrap`, `qemu-user-static`, `curl`, and `zstd`. Then run:

```bash
packaging/proxmox/build-template.sh \
  --version 0.12.0-rc.1 \
  --commit "$(git rev-parse HEAD)" \
  --architecture amd64
```

Use `--architecture arm64` for the ARM template. Cross-architecture builds use `qemu-debootstrap`; the emulator is removed from the finished root filesystem.

The output is `artifacts/speedtest-dashboard_<version>_<architecture>.tar.zst` plus a SHA-256 file. A real Proxmox host must still validate upload, `pct create`, unprivileged boot, systemd, HTTP, persistence, and provider operation before a release claims native compatibility.

## Layout

- `/opt/speedtest-dashboard`: self-contained application, web assets, LibreSpeed CLI, and notices
- `/etc/speedtest-dashboard`: environment and release metadata
- `/var/lib/speedtest-dashboard`: SQLite database and Data Protection keys
- `/etc/systemd/system/speedtest-dashboard.service`: native service

The template includes LibreSpeed CLI `1.0.13` from commit `2f2408764d88e9601aa64a03b340f8e3151003e4`. It does not include the Ookla CLI.
