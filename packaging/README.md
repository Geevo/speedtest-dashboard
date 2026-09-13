# Deployment and packaging

Choose the environment where the dashboard will run:

| Target | Build entry point | Installation guide |
| --- | --- | --- |
| Docker / Podman | `containers/Dockerfile` | [OCI containers](containers/README.md) |
| Native Proxmox LXC | `proxmox/build-template.sh` | [Proxmox LXC](proxmox/README.md) |

Commands in both guides run from the repository root. OCI builds use the root
as their build context even though their Dockerfile lives here. The Proxmox
builder creates a Debian root filesystem independently of the OCI image.

`providers/` holds the shared LibreSpeed and M-Lab source builds,
checksum-pinned fast-cli and Ookla downloads, and the Ookla notice. Both targets
include LibreSpeed, fast-cli, and the official M-Lab NDT7 client by default and
can opt into Ookla locally; public release jobs exclude Ookla. See [Enable
Ookla](../docs/ookla.md).

Build outputs belong in the ignored `artifacts/` directory or outside the
checkout. Commit build recipes and sanitized fixtures, never appliances,
runtime state, credentials, or personal evaluation reports.
