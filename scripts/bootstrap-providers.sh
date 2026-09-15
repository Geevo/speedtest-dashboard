#!/usr/bin/env sh
# LibreSpeed and FAST.com are installed by default because verified prebuilt binaries are
# available for every supported debug platform. M-Lab is opt-in because it must be built
# from pinned source with a local Go toolchain. Ookla is opt-in because its CLI is
# proprietary: downloading it requires explicit acknowledgement of both the licence and
# GDPR/privacy terms, and runtime acceptance remains a separate explicit configuration.
set -eu

usage() {
  cat <<'EOF'
Usage: ./scripts/bootstrap-providers.sh [options]

Options:
  --include-mlab             Build and install the pinned M-Lab NDT7 client (requires Go).
  --include-ookla            Download and install the pinned Ookla CLI (requires dpkg-deb).
  --accept-ookla-license     Confirm that you accept Ookla's licence and terms.
  --accept-ookla-gdpr        Confirm that you accept Ookla's privacy/GDPR terms.
  -h, --help                 Show this help.

Set PROVIDER_DIR to override the installation directory.
EOF
}

include_mlab=false
include_ookla=false
accept_ookla_license=false
accept_ookla_gdpr=false

while [ "$#" -gt 0 ]; do
  case "$1" in
    --include-mlab) include_mlab=true ;;
    --include-ookla) include_ookla=true ;;
    --accept-ookla-license) accept_ookla_license=true ;;
    --accept-ookla-gdpr) accept_ookla_gdpr=true ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done

if [ "$include_ookla" = true ] && {
  [ "$accept_ookla_license" != true ] || [ "$accept_ookla_gdpr" != true ];
}; then
  echo "Ookla bootstrap requires --accept-ookla-license and --accept-ookla-gdpr." >&2
  echo "Review https://www.speedtest.net/about/eula, /terms, and /privacy first." >&2
  exit 2
fi

if [ "$include_ookla" != true ] && {
  [ "$accept_ookla_license" = true ] || [ "$accept_ookla_gdpr" = true ];
}; then
  echo "Ookla acceptance options require --include-ookla." >&2
  exit 2
fi

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(dirname "$script_dir")
destination=${PROVIDER_DIR:-"${HOME:?HOME must be set}/.local/share/speedtest-dashboard/development/providers"}
librespeed_version=1.0.13
fast_version=0.3.5

case "$(uname -m)" in
  x86_64|amd64)
    target_arch=amd64
    librespeed_arch=amd64
    librespeed_sha256=33f2278a6ae16e83dc80f38a16aa8689b0b315530ce30ccb6de7968a2bf7527a
    fast_arch=x86_64
    fast_sha256=0973a2e5ff552fb2bd0b64359101f919ff22086aafd7be571aa9d2289295bbc4
    ;;
  aarch64|arm64)
    target_arch=arm64
    librespeed_arch=arm64
    librespeed_sha256=3f59e01ea03626168a0ff3d6f3371a38c054267ee500a5f9a644d3fe28ceed69
    fast_arch=aarch64
    fast_sha256=e0bd40ca25027721466c59c1752630f911a8d5efdbfe39d7ef348143340a7664
    ;;
  *)
    echo "Unsupported architecture: $(uname -m)" >&2
    exit 1
    ;;
esac

if [ "$include_mlab" = true ] && ! command -v go >/dev/null 2>&1; then
  echo "M-Lab bootstrap requires Go on PATH." >&2
  exit 1
fi

if [ "$include_ookla" = true ] && ! command -v dpkg-deb >/dev/null 2>&1; then
  echo "Ookla bootstrap requires dpkg-deb on PATH to extract its official package." >&2
  exit 1
fi

work_dir=$(mktemp -d)
cleanup() {
  rm -rf "$work_dir"
}
trap cleanup EXIT HUP INT TERM

librespeed_archive="$work_dir/librespeed.tar.gz"
fast_archive="$work_dir/fast.tar.gz"

curl --fail --location --silent --show-error --retry 3 \
  "https://github.com/librespeed/speedtest-cli/releases/download/v${librespeed_version}/librespeed-cli_${librespeed_version}_linux_${librespeed_arch}.tar.gz" \
  --output "$librespeed_archive"
printf '%s  %s\n' "$librespeed_sha256" "$librespeed_archive" | sha256sum --check --strict -

curl --fail --location --silent --show-error --retry 3 \
  "https://github.com/mikkelam/fast-cli/releases/download/v${fast_version}/fast-cli-${fast_arch}-linux.tar.gz" \
  --output "$fast_archive"
printf '%s  %s\n' "$fast_sha256" "$fast_archive" | sha256sum --check --strict -

[ "$(tar -tzf "$librespeed_archive")" = "LICENSE
librespeed-cli" ] || {
  echo "LibreSpeed archive contains unexpected files" >&2
  exit 1
}
[ "$(tar -tzf "$fast_archive")" = "fast-cli
LICENSE" ] || {
  echo "FAST.com archive contains unexpected files" >&2
  exit 1
}

mkdir -p "$destination"
tar -xzf "$librespeed_archive" -C "$work_dir" librespeed-cli LICENSE
install -m 0755 "$work_dir/librespeed-cli" "$destination/librespeed-cli"
install -m 0644 "$work_dir/LICENSE" "$destination/LICENSE.librespeed-cli"

rm "$work_dir/LICENSE"
tar -xzf "$fast_archive" -C "$work_dir" fast-cli LICENSE
install -m 0755 "$work_dir/fast-cli" "$destination/fast-cli"
install -m 0644 "$work_dir/LICENSE" "$destination/LICENSE.fast-cli"

providers="LibreSpeed ${librespeed_version}, FAST.com ${fast_version}"

if [ "$include_mlab" = true ]; then
  mlab_output="$work_dir/mlab"
  TARGETARCH="$target_arch" OUTPUT_DIR="$mlab_output" \
    sh "$repo_root/packaging/providers/build-mlab.sh"
  install -m 0755 "$mlab_output/mlab-ndt7-client" "$destination/mlab-ndt7-client"
  install -m 0644 "$mlab_output/LICENSE.mlab-ndt7-client" "$destination/LICENSE.mlab-ndt7-client"
  providers="$providers, M-Lab 0.10.1"
fi

if [ "$include_ookla" = true ]; then
  ookla_download="$work_dir/ookla-download"
  ookla_root="$work_dir/ookla-root"
  TARGETARCH="$target_arch" OUTPUT_DIR="$ookla_download" \
    sh "$repo_root/packaging/providers/download-ookla.sh"
  mkdir -p "$ookla_root"
  dpkg-deb --extract "$ookla_download/ookla-speedtest.deb" "$ookla_root"
  install -m 0755 "$ookla_root/usr/bin/speedtest" "$destination/speedtest"
  install -m 0644 "$repo_root/packaging/providers/OOKLA_NOTICE.md" "$destination/OOKLA_NOTICE.md"
  providers="$providers, Ookla 1.2.0.84"
fi

echo "Bootstrapped $providers providers in $destination"
if [ "$include_ookla" = true ]; then
  echo "Ookla runtime acceptance is not stored by this script; set Providers__Ookla__AcceptLicense=true and Providers__Ookla__AcceptGdpr=true when launching the app."
fi
