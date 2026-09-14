# Enable Ookla

The application already includes the Ookla provider. Public OCI images and
Proxmox templates exclude the official CLI; you can add it in a local build.
Both build paths use [one download script](../packaging/providers/download-ookla.sh)
with pinned amd64 and arm64 checksums for package `1.2.0.84-1.ea6b6773cf`.

Before enabling it, review Ookla's [licence](https://www.speedtest.net/about/eula),
[terms](https://www.speedtest.net/about/terms), and
[privacy policy](https://www.speedtest.net/about/privacy). Installation does not
accept those terms. Set both runtime acceptance values only if you accept them.
See the [notice](../packaging/providers/OOKLA_NOTICE.md).

Run the build commands below from the repository root.

## Docker or Podman

Build and start with Ookla, after accepting its terms:

```bash
make run INSTALL_OOKLA=true OOKLA_ACCEPT_LICENSE=true OOKLA_ACCEPT_GDPR=true
```

This works the same on Docker and Podman; `make` uses whichever you have. Keep
these same variables when recreating the service, since both acceptance values
default to `false` when omitted.

For a build only:

```bash
make image INSTALL_OOKLA=true TAG=speedtest-dashboard:local-ookla
```

When running that image in your own deployment, set:

```yaml
environment:
  Providers__Ookla__Enabled: "true"
  Providers__Ookla__AcceptLicense: "true"
  Providers__Ookla__AcceptGdpr: "true"
```

The standard `compose.yml` consumes published images. Use `compose.local.yml`
for the build-and-start command above. See the [container guide](../packaging/containers/README.md)
for persistence, project names, and HTTPS configuration.

## Native Proxmox LXC

Add one flag to the normal native build:

```bash
make proxmox-template INSTALL_OOKLA=true
```

Use `ARCH=arm64` for ARM. This builds inside the pinned toolchain container, so
Make and Docker or Podman are the only prerequisites; the
[Proxmox guide](../packaging/proxmox/README.md) covers building on a host that
already has the full toolchain.
The output is `artifacts/speedtest-dashboard_<version>_<architecture>_ookla.tar.zst`
with a checksum file. Install it using the normal Proxmox template procedure,
substituting this filename.

Inside the resulting LXC container, after accepting the terms:

```bash
sed -i \
  -e 's/^Providers__Ookla__AcceptLicense=false$/Providers__Ookla__AcceptLicense=true/' \
  -e 's/^Providers__Ookla__AcceptGdpr=false$/Providers__Ookla__AcceptGdpr=true/' \
  /etc/speedtest-dashboard/environment
systemctl restart speedtest-dashboard
```

## Existing installations and verification

If you install the official CLI yourself, its default path is `/usr/bin/speedtest`.
Set `Providers__Ookla__ExecutablePath` if you use a different path, and set both
acceptance variables above. The CLI must match the runtime architecture and be
executable by the service user. An OCI container must contain the binary and its
runtime libraries; installing it on the host alone does not install it inside
the container.

Open the Ookla page to check provider readiness. A missing CLI and unaccepted
terms are reported separately. Checking readiness does not run a bandwidth test.
To disable the integration, set `Providers__Ookla__Enabled=false` and recreate
the container or restart the LXC service.

Public release jobs always build without Ookla and publish only the standard
template filenames. Keep local Ookla artifacts out of public release uploads.
