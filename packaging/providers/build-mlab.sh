#!/bin/sh
set -eu

: "${TARGETARCH:?TARGETARCH must be amd64 or arm64}"

MLAB_VERSION="${MLAB_VERSION:-0.10.1}"
MLAB_COMMIT="${MLAB_COMMIT:-4a5f6325d1d586ab38afb84566a5781b5d6c3d9a}"
MLAB_SOURCE_SHA256="${MLAB_SOURCE_SHA256:-6aae19abb130d53452ceeef2452ece3a54e653df745e02037726ed9742551ab6}"
OUTPUT_DIR="${OUTPUT_DIR:-/out}"

case "$TARGETARCH" in
  amd64) go_arch=amd64 ;;
  arm64) go_arch=arm64 ;;
  *) echo "Unsupported M-Lab target architecture: $TARGETARCH" >&2; exit 1 ;;
esac

build_dir="$(mktemp -d)"
trap 'rm -rf "$build_dir"' EXIT HUP INT TERM

archive="$build_dir/source.tar.gz"
source_dir="$build_dir/source"
mkdir -p "$source_dir" "$OUTPUT_DIR"
curl --fail --location --silent --show-error \
  "https://github.com/m-lab/ndt7-client-go/archive/${MLAB_COMMIT}.tar.gz" \
  --output "$archive"
echo "$MLAB_SOURCE_SHA256  $archive" | sha256sum --check --strict -
tar --extract --gzip --file "$archive" --strip-components=1 --directory "$source_dir"

(
  cd "$source_dir"
  CGO_ENABLED=0 GOOS=linux GOARCH="$go_arch" go build \
    -trimpath \
    -buildvcs=false \
    -ldflags="-s -w -X main.ClientVersion=${MLAB_VERSION}" \
    -o "$OUTPUT_DIR/mlab-ndt7-client" \
    ./cmd/ndt7-client
  cp LICENSE "$OUTPUT_DIR/LICENSE.mlab-ndt7-client"
)
