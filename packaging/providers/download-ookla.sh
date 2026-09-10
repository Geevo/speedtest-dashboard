#!/bin/sh
# Download only. Installation and runtime terms acceptance belong to the caller.
set -eu

: "${TARGETARCH:?TARGETARCH must be amd64 or arm64}"
: "${OUTPUT_DIR:?OUTPUT_DIR must name a download directory}"

package_version=1.2.0.84-1.ea6b6773cf
case "$TARGETARCH" in
  amd64) package_sha256=35e084567a6388631fb10cf01e5e0d6b57a67d34ede2b72ba111b3d9164c8b94 ;;
  arm64) package_sha256=98e7de9db3bf181d08bc67e647bcfc71349c8014e387289c08e54e5c55d82f37 ;;
  *) echo "Unsupported Ookla target architecture: $TARGETARCH" >&2; exit 1 ;;
esac

mkdir -p "$OUTPUT_DIR"
download_dir=$(mktemp -d "$OUTPUT_DIR/.ookla-download.XXXXXX")
trap 'rm -rf "$download_dir"' EXIT HUP INT TERM
package="speedtest_${package_version}_${TARGETARCH}.deb"
curl --fail --location --silent --show-error \
  "https://packagecloud.io/ookla/speedtest-cli/packages/debian/bookworm/$package/download.deb?distro_version_id=215" \
  --output "$download_dir/ookla-speedtest.deb"
printf '%s  %s\n' "$package_sha256" "$download_dir/ookla-speedtest.deb" | sha256sum --check --strict -
mv "$download_dir/ookla-speedtest.deb" "$OUTPUT_DIR/ookla-speedtest.deb"
