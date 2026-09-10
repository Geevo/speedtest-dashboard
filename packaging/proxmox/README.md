# Proxmox LXC

This tool builds a Debian 12 (`bookworm`) root filesystem for Proxmox VE. The application runs directly under systemd as the unprivileged `speedtest` user. Docker and Podman are not used at runtime.

## Install a release template

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

The target platform is Proxmox VE 9 with a Debian 12 root filesystem. Use the [release validation checklist](../../docs/releasing.md) on a real host before claiming compatibility; archive creation alone is not runtime validation.

Inside the container:

```bash
systemctl status speedtest-dashboard
journalctl -u speedtest-dashboard
curl -fsS http://127.0.0.1:8080/api/health
```

The dashboard listens on port 8080. Configure Proxmox firewall access yourself. The appliance does not change Proxmox networking, DNS, time, or firewall settings and does not install SSH. Use `pct console` or `pct enter` for administration.

New templates are intended for new installations. Before attempting a manual in-place update, back up the container and `/var/lib/speedtest-dashboard`. A native package/update mechanism is not part of this release.

## Build from source

Install .NET SDK 10.0.400, Node.js 22.23.2, npm, Go 1.27.1, `debootstrap`, `debian-archive-keyring`, `qemu-user-static`, `curl`, and `zstd`. Run all commands from the repository root:

```bash
packaging/proxmox/build-template.sh \
  --version 0.12.0-rc.1 \
  --commit "$(git rev-parse HEAD)" \
  --architecture amd64
```

Use `--architecture arm64` for the ARM template. Cross-architecture builds use `qemu-debootstrap`; the emulator is removed from the finished root filesystem.

The default template excludes the proprietary Ookla CLI. Add `--install-ookla` for a local build containing the pinned, checksum-verified package. See [Enable Ookla](../../docs/ookla.md) for the build command and runtime acceptance settings.

The default output is `artifacts/speedtest-dashboard_<version>_<architecture>.tar.zst` plus a SHA-256 file. `--install-ookla` produces `artifacts/speedtest-dashboard_<version>_<architecture>_ookla.tar.zst` instead. A real Proxmox host must still validate upload, `pct create`, unprivileged boot, systemd, HTTP, persistence, and provider operation before a release claims native compatibility.

### Build using a container toolchain

On a Linux host without the Debian bootstrap tools, use the version-pinned builder container. It needs a privileged build container for debootstrap; the resulting appliance runs natively. Docker can use the same commands by replacing `podman` with `docker`.

```bash
podman build \
  --file packaging/proxmox/Containerfile.builder \
  --tag speedtest-dashboard-proxmox-builder .

podman run --rm --privileged \
  --volume "$PWD:/source:Z" \
  speedtest-dashboard-proxmox-builder \
  packaging/proxmox/build-template.sh \
    --version 0.12.0-rc.1 \
    --commit "$(git rev-parse HEAD)" \
    --architecture amd64
```

## Layout

- `/opt/speedtest-dashboard`: self-contained application, web assets, LibreSpeed CLI, and notices
- `/etc/speedtest-dashboard`: environment and release metadata
- `/var/lib/speedtest-dashboard`: SQLite database and Data Protection keys
- `/etc/systemd/system/speedtest-dashboard.service`: native service

The template includes LibreSpeed CLI `1.0.13` from commit `2f2408764d88e9601aa64a03b340f8e3151003e4`. Default builds do not include the Ookla CLI.

The Debian root filesystem includes `ifupdown2`, `iproute2`, `iputils-ping`, `isc-dhcp-client`, and `systemd-sysv` so Proxmox can configure and bring up the container network using its normal Debian LXC integration.

[Deployment overview](../README.md) · [Release validation](../../docs/releasing.md)
