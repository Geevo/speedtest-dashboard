# Release maintenance

The [release workflow](../.github/workflows/release.yml) verifies the application
and then builds two independent targets from the same version tag and commit:

- a Docker/Podman OCI manifest for `linux/amd64` and `linux/arm64`;
- native Debian Proxmox LXC templates for `amd64` and `arm64`.

Both include LibreSpeed and fast-cli. Public jobs set `INSTALL_OOKLA=false` for OCI and omit
`--install-ookla` for LXC. The native release upload uses explicit standard
filenames, excluding local `_ookla` variants. Checksums and SBOMs accompany the
templates; OCI publication enables SBOM and provenance attestations.

## Before tagging

1. Run the checks in [CONTRIBUTING.md](../CONTRIBUTING.md).
2. Review the staged file list and check for ignored tracked files with
   `git ls-files -ci --exclude-standard`. Keep runtime databases, Data Protection
   keys, local `.env` files, generated archives, and personal assessments out of
   Git. Use `.local/` for disposable local notes or keep them outside the checkout.
3. Review history before making an existing private repository public. Deleting
   a file or adding it to `.gitignore` does not remove earlier commits. Decide
   whether to publish a fresh history or remove unwanted historical paths before
   pushing the public repository. Rotate any credential that was committed.
4. Set the repository owner in deployment examples and verify GHCR visibility.
5. Update the changelog and version. Start with a release candidate when a
   platform has not yet been validated.

## Validate the candidate

For OCI, verify both manifest architectures, public pull access, health,
non-root execution, read-only-root operation, packaged provider health, and state surviving container
replacement in Docker and rootless Podman. Inspect SBOM/provenance and confirm
that no Ookla CLI, SDK, source tree, fixtures, or local data reached the image.

For each supported Proxmox architecture, verify the checksums and then upload
the template to a real Proxmox VE 9 host. Create an unprivileged container with
no nesting and validate boot, networking, systemd, HTTP access, persisted state
after restart, authentication, API access, schedules, and results. Run provider
tests deliberately, accounting for the bandwidth they consume. Verify that the
public template excludes Ookla and contains the expected LibreSpeed and fast-cli licences.

Record machine-specific measurements and evaluation results outside the
repository. This document describes a repeatable procedure, not a claim that
a candidate has passed it. Promote to a stable release only after validation.
