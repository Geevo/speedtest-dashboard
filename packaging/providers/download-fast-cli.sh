#!/usr/bin/env sh
set -eu

version=${FAST_CLI_VERSION:-0.3.5}
target_arch=${TARGETARCH:-}
output_dir=${OUTPUT_DIR:-/out}

case "$target_arch" in
  amd64)
    upstream_arch=x86_64
    expected_sha256=${FAST_CLI_SHA256:-0973a2e5ff552fb2bd0b64359101f919ff22086aafd7be571aa9d2289295bbc4}
    ;;
  arm64)
    upstream_arch=aarch64
    expected_sha256=${FAST_CLI_SHA256:-e0bd40ca25027721466c59c1752630f911a8d5efdbfe39d7ef348143340a7664}
    ;;
  *)
    echo "Unsupported FAST.com CLI target architecture: $target_arch" >&2
    exit 1
    ;;
esac

case "$version" in
  *[!0-9.]*|'')
    echo "Invalid FAST.com CLI version: $version" >&2
    exit 1
    ;;
esac

case "$expected_sha256" in
  *[!0-9a-f]*|'')
    echo "Invalid FAST.com CLI SHA-256 value" >&2
    exit 1
    ;;
esac
[ "${#expected_sha256}" -eq 64 ] || {
  echo "FAST.com CLI SHA-256 value must contain 64 hexadecimal characters" >&2
  exit 1
}

work_dir=$(mktemp -d)
cleanup() {
  rm -rf "$work_dir"
}
trap cleanup EXIT HUP INT TERM

asset="fast-cli-${upstream_arch}-linux.tar.gz"
archive="$work_dir/$asset"
url="https://github.com/mikkelam/fast-cli/releases/download/v${version}/${asset}"

curl --fail --location --silent --show-error --retry 3 --output "$archive" "$url"
printf '%s  %s\n' "$expected_sha256" "$archive" | sha256sum --check --status || {
  echo "FAST.com CLI archive checksum verification failed" >&2
  exit 1
}

archive_contents=$(tar -tzf "$archive")
[ "$archive_contents" = "fast-cli
LICENSE" ] || {
  echo "FAST.com CLI archive contains unexpected files" >&2
  exit 1
}

tar -xzf "$archive" -C "$work_dir" fast-cli LICENSE
mkdir -p "$output_dir"
install -m 0755 "$work_dir/fast-cli" "$output_dir/fast-cli"
install -m 0644 "$work_dir/LICENSE" "$output_dir/LICENSE.fast-cli"
