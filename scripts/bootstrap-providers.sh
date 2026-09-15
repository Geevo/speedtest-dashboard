#!/usr/bin/env sh
set -eu

destination=${PROVIDER_DIR:-"${HOME:?HOME must be set}/.local/share/speedtest-dashboard/development/providers"}
librespeed_version=1.0.13
fast_version=0.3.5

case "$(uname -m)" in
  x86_64|amd64)
    librespeed_arch=amd64
    librespeed_sha256=33f2278a6ae16e83dc80f38a16aa8689b0b315530ce30ccb6de7968a2bf7527a
    fast_arch=x86_64
    fast_sha256=0973a2e5ff552fb2bd0b64359101f919ff22086aafd7be571aa9d2289295bbc4
    ;;
  aarch64|arm64)
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

echo "Bootstrapped LibreSpeed ${librespeed_version} and FAST.com ${fast_version} providers in $destination"
