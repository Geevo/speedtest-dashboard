#!/bin/sh
set -eu

: "${TARGETARCH:?TARGETARCH must be amd64 or arm64}"

LIBRESPEED_VERSION="${LIBRESPEED_VERSION:-1.0.13}"
LIBRESPEED_COMMIT="${LIBRESPEED_COMMIT:-2f2408764d88e9601aa64a03b340f8e3151003e4}"
LIBRESPEED_SOURCE_SHA256="${LIBRESPEED_SOURCE_SHA256:-5e6622697bf5b651d552458997856dcd7f86e07e4bd44508aaad1be238085b11}"
LIBRESPEED_BUILD_DATE="${LIBRESPEED_BUILD_DATE:-2026-04-30T06:42:34Z}"
OUTPUT_DIR="${OUTPUT_DIR:-/out}"

case "$TARGETARCH" in
  amd64) go_arch=amd64 ;;
  arm64) go_arch=arm64 ;;
  *) echo "Unsupported LibreSpeed target architecture: $TARGETARCH" >&2; exit 1 ;;
esac

build_dir="$(mktemp -d)"
trap 'rm -rf "$build_dir"' EXIT HUP INT TERM

archive="$build_dir/source.tar.gz"
source_dir="$build_dir/source"
mkdir -p "$source_dir" "$OUTPUT_DIR"
curl --fail --location --silent --show-error \
  "https://github.com/librespeed/speedtest-cli/archive/${LIBRESPEED_COMMIT}.tar.gz" \
  --output "$archive"
echo "$LIBRESPEED_SOURCE_SHA256  $archive" | sha256sum --check --strict -
tar --extract --gzip --file "$archive" --strip-components=1 --directory "$source_dir"

(
  cd "$source_dir"
  CGO_ENABLED=0 GOOS=linux GOARCH="$go_arch" go build \
    -trimpath \
    -buildvcs=false \
    -ldflags="-s -w -X github.com/librespeed/speedtest-cli/defs.ProgName=librespeed-cli -X github.com/librespeed/speedtest-cli/defs.ProgVersion=v${LIBRESPEED_VERSION} -X github.com/librespeed/speedtest-cli/defs.BuildDate=${LIBRESPEED_BUILD_DATE}" \
    -o "$OUTPUT_DIR/librespeed-cli" \
    .
  cp LICENSE "$OUTPUT_DIR/LICENSE.librespeed-cli"
)
