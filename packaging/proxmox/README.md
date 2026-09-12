# Proxmox LXC

This is the native option: a small Debian 12 root filesystem, the dashboard, LibreSpeed, and a systemd service. There is no Docker or Podman inside the LXC.

The template is a good fit when your router or Proxmox network already sends this container through the VPN exit you want to measure. It does not configure a VPN by itself. If Gluetun owns your VPN connection, the [container setup](../containers/README.md#routing-tests-through-gluetun) is usually the more natural choice.

## Download a release template

Release assets follow this pattern:

- `speedtest-dashboard_<version>_amd64.tar.zst`
- `speedtest-dashboard_<version>_arm64.tar.zst`
- `SHA256SUMS`

On a normal x86-64 Proxmox host, download the `amd64` archive straight into local template storage. Replace `<release-url>` with the release's asset URL:

```bash
cd /var/lib/vz/template/cache

wget <release-url>/speedtest-dashboard_0.12.0-rc.1_amd64.tar.zst
wget <release-url>/SHA256SUMS

grep 'speedtest-dashboard_0.12.0-rc.1_amd64.tar.zst$' SHA256SUMS \
  | sha256sum --check -
```

Do not carry on if the checksum fails.

If your template storage is not `local`, upload the archive through the Proxmox UI or copy it into that storage's `template/cache` directory, then use its storage ID in the next command.

## Create the container

Choose a free CT ID and the bridge/VLAN settings that match your lab:

```bash
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

A VLAN-aware example would change the network option to something like:

```bash
--net0 name=eth0,bridge=vmbr0,tag=30,ip=dhcp
```

That is ordinary Proxmox networking; the template does not touch bridges, VLANs, DNS, gateways, time, or firewall rules.

Find the assigned address:

```bash
pct exec 120 -- hostname -I
```

Then open `http://<container-ip>:8080`.

The target is Proxmox VE 9 with a Debian 12 (`bookworm`) root filesystem. Release archives still need validation on a real PVE host before compatibility is claimed; successfully building an archive is not the same as successfully booting it.

## First checks

```bash
pct exec 120 -- systemctl status speedtest-dashboard
pct exec 120 -- curl -fsS http://127.0.0.1:8080/api/health
pct exec 120 -- journalctl -u speedtest-dashboard -n 100 --no-pager
```

Or jump into the container:

```bash
pct enter 120
```

The app listens on port `8080` by default. To use another port, change `DASHBOARD_PORT` in `/etc/speedtest-dashboard/environment`, restart the service, and open that port in the Proxmox firewall. The template does not install SSH; `pct console`, `pct enter`, and `pct exec` are the intended starting points.

Fresh installs open anonymously. Put the dashboard behind HTTPS before enabling login protection. For an isolated HTTP-only lab, edit `/etc/speedtest-dashboard/environment`, set `Authentication__AllowInsecureHttp=true`, and restart the service before creating the login.

## Pointing the LXC through a VPN

The dashboard sees whatever default route and DNS the LXC receives. Common homelab options are:

- put the CT on a VLAN whose gateway uses a VPN policy route
- give the CT a gateway handled by a dedicated VPN router/VM
- configure a native WireGuard/OpenVPN client yourself inside the CT

The appliance does not manage any of those routes. Once your network is in place, compare the dashboard's Network panel with the exit IP and country you expect. That confirms what the backend observed, but it is not a substitute for firewall and leak testing.

## Files and service layout

| Path | Purpose |
| --- | --- |
| `/opt/speedtest-dashboard` | Application, web assets, LibreSpeed CLI, and notices |
| `/etc/speedtest-dashboard/environment` | Environment-based configuration |
| `/etc/speedtest-dashboard/release` | Template build metadata |
| `/var/lib/speedtest-dashboard` | SQLite database and Data Protection keys |
| `/etc/systemd/system/speedtest-dashboard.service` | systemd unit |

The service runs as the unprivileged `speedtest` user with no extra capabilities. The LXC itself should also stay unprivileged.

The template includes LibreSpeed CLI `1.0.13` from commit `2f2408764d88e9601aa64a03b340f8e3151003e4`. Public builds do not contain the proprietary Ookla CLI.

After changing configuration:

```bash
systemctl restart speedtest-dashboard
journalctl -u speedtest-dashboard -f
```

See the [configuration reference](../../docs/configuration.md) for every supported setting. Remember that native installs use `/var/lib/speedtest-dashboard` rather than the container image's `/data` defaults.

## Backups and updates

Back up the whole LXC with normal Proxmox tooling. If you are copying files manually, stop the service first so SQLite and its WAL file are consistent:

```bash
systemctl stop speedtest-dashboard
# Copy /var/lib/speedtest-dashboard with your normal backup tool.
systemctl start speedtest-dashboard
```

Release templates are currently intended for new containers. There is no native package repository or in-place update command yet. Before attempting a manual replacement, back up the CT and `/var/lib/speedtest-dashboard`. Database downgrades are not supported.

## Build a template from source

Install .NET SDK 10.0.400, Node.js 22.23.2, npm, Go 1.27.1, `debootstrap`, `debian-archive-keyring`, `qemu-user-static`, `curl`, and `zstd`. From the repository root:

```bash
packaging/proxmox/build-template.sh \
  --version 0.12.0-rc.1 \
  --commit "$(git rev-parse HEAD)" \
  --architecture amd64
```

Use `--architecture arm64` for the ARM template. Cross-architecture builds use `qemu-debootstrap`; the emulator is removed from the finished root filesystem.

The default output is `artifacts/speedtest-dashboard_<version>_<architecture>.tar.zst` plus a SHA-256 file. Public-style builds exclude Ookla. Add `--install-ookla` only for your own build after reading [Enable Ookla](../../docs/ookla.md).

A real PVE host should still verify upload, `pct create`, unprivileged boot, systemd, HTTP, persistence, and provider operation.

### Build with the pinned container toolchain

On Linux without the Debian bootstrap tools, use the included builder container. The builder itself needs `--privileged` for `debootstrap`; the finished appliance does not.

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

Docker can use the same commands by replacing `podman` with `docker`.

[Back to the main README](../../README.md) · [Release validation](../../docs/releasing.md)
