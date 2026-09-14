# Deployment and packaging

Choose the environment where the dashboard will run:

| Target | Build it | Installation guide |
| --- | --- | --- |
| Docker / Podman | `make image` | [OCI containers](containers/README.md) |
| Native Proxmox LXC | `make proxmox-template` | [Proxmox LXC](proxmox/README.md) |

Both build from a fresh clone with Make and Docker or Podman as the only
prerequisites. Run `make doctor` to confirm the machine is ready, or `make help`
for every target and override.

Commands in both guides run from the repository root. OCI builds use the root
as their build context even though their Dockerfile lives here. The Proxmox
builder creates a Debian root filesystem independently of the OCI image, using
the pinned toolchain in `proxmox/Containerfile.builder`.

`providers/` holds the shared LibreSpeed and M-Lab source builds,
checksum-pinned fast-cli and Ookla downloads, and the Ookla notice. Both targets
include LibreSpeed, fast-cli, and the official M-Lab NDT7 client by default and
can opt into Ookla locally; public release jobs exclude Ookla. See [Enable
Ookla](../docs/ookla.md).

Build outputs belong in the ignored `artifacts/` directory or outside the
checkout. Commit build recipes and non-sensitive test fixtures, not appliances,
runtime state, credentials, or local test results.
