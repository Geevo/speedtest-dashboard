#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 --version VERSION --architecture amd64|arm64 [--commit SHA] [--output DIRECTORY] [--install-ookla]" >&2
  exit 2
}

version=
architecture=
commit=unknown
output=
install_ookla=false

while [ "$#" -gt 0 ]; do
  case "$1" in
    --version) [ "$#" -ge 2 ] || usage; version=$2; shift 2 ;;
    --architecture) [ "$#" -ge 2 ] || usage; architecture=$2; shift 2 ;;
    --commit) [ "$#" -ge 2 ] || usage; commit=$2; shift 2 ;;
    --output) [ "$#" -ge 2 ] || usage; output=$2; shift 2 ;;
    --install-ookla) install_ookla=true; shift ;;
    *) usage ;;
  esac
done

[ -n "$version" ] || usage
case "$architecture" in
  amd64) runtime=linux-x64; qemu_binary=qemu-x86_64-static ;;
  arm64) runtime=linux-arm64; qemu_binary=qemu-aarch64-static ;;
  *) usage ;;
esac

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/../.." && pwd)
output=${output:-"$repo_root/artifacts"}
mkdir -p "$output"
output=$(CDPATH= cd -- "$output" && pwd)

if [ "$(id -u)" -eq 0 ]; then
  elevate=
elif command -v sudo >/dev/null 2>&1; then
  elevate=sudo
else
  echo "Root privileges or sudo are required to bootstrap the root filesystem." >&2
  exit 1
fi

for command in debootstrap dotnet npm go curl dpkg sha256sum tar zstd; do
  command -v "$command" >/dev/null 2>&1 || {
    echo "Required command not found: $command" >&2
    exit 1
  }
done

debian_keyring=/usr/share/keyrings/debian-archive-keyring.gpg
if [ ! -r "$debian_keyring" ]; then
  echo "Required Debian archive keyring not found: $debian_keyring" >&2
  exit 1
fi

host_architecture=$(dpkg --print-architecture)
bootstrap=debootstrap
if [ "$host_architecture" != "$architecture" ]; then
  command -v qemu-debootstrap >/dev/null 2>&1 || {
    echo "qemu-debootstrap is required for a cross-architecture template." >&2
    exit 1
  }
  bootstrap=qemu-debootstrap
fi

work_dir=$(mktemp -d)
cleanup() {
  if [ -n "$elevate" ]; then
    $elevate rm -rf "$work_dir"
  else
    rm -rf "$work_dir"
  fi
}
trap cleanup EXIT HUP INT TERM

web_dist="$work_dir/web-dist"
publish_dir="$work_dir/publish"
librespeed_dir="$work_dir/librespeed"
fastcom_dir="$work_dir/fastcom"
rootfs="$work_dir/rootfs"
artifact_suffix=
if [ "$install_ookla" = true ]; then
  artifact_suffix=_ookla
fi
artifact="$output/speedtest-dashboard_${version}_${architecture}${artifact_suffix}.tar.zst"

npm ci --prefix "$repo_root/src/SpeedtestDashboard.Web"
npm run build --prefix "$repo_root/src/SpeedtestDashboard.Web"
mkdir -p "$web_dist"
cp -R "$repo_root/src/SpeedtestDashboard.Web/dist/." "$web_dist/"

dotnet restore "$repo_root/src/SpeedtestDashboard.Api/SpeedtestDashboard.Api.csproj" \
  --locked-mode \
  -p:AuditPipeline=true
dotnet publish "$repo_root/src/SpeedtestDashboard.Api/SpeedtestDashboard.Api.csproj" \
  --configuration Release \
  --no-restore \
  --runtime "$runtime" \
  --self-contained true \
  --output "$publish_dir" \
  -p:FrontendDist="$web_dist" \
  -p:DebugSymbols=false \
  -p:DebugType=None \
  -p:IsTransformWebConfigDisabled=true \
  -p:PublishSingleFile=false \
  -p:SourceRevisionId="$commit" \
  -p:Version="$version"

TARGETARCH="$architecture" OUTPUT_DIR="$librespeed_dir" "$repo_root/packaging/providers/build-librespeed.sh"
TARGETARCH="$architecture" OUTPUT_DIR="$fastcom_dir" "$repo_root/packaging/providers/download-fast-cli.sh"

$elevate "$bootstrap" \
  --arch="$architecture" \
  --keyring="$debian_keyring" \
  --variant=minbase \
  --include=ca-certificates,curl,ifupdown2,iproute2,iputils-ping,isc-dhcp-client,libicu72,libssl3,systemd,systemd-sysv,tzdata,zlib1g \
  bookworm \
  "$rootfs" \
  http://deb.debian.org/debian

if [ "$install_ookla" = true ]; then
  TARGETARCH="$architecture" OUTPUT_DIR="$work_dir/ookla" \
    "$repo_root/packaging/providers/download-ookla.sh"
  $elevate install -m 0644 "$work_dir/ookla/ookla-speedtest.deb" "$rootfs/tmp/ookla-speedtest.deb"
  $elevate chroot "$rootfs" dpkg --install /tmp/ookla-speedtest.deb
  $elevate rm -f "$rootfs/tmp/ookla-speedtest.deb"
fi

$elevate chroot "$rootfs" groupadd --system speedtest
$elevate chroot "$rootfs" useradd \
  --system \
  --gid speedtest \
  --home-dir /var/lib/speedtest-dashboard \
  --shell /usr/sbin/nologin \
  speedtest
service_uid=$($elevate chroot "$rootfs" id -u speedtest)
service_gid=$($elevate chroot "$rootfs" id -g speedtest)

$elevate install -d -m 0755 "$rootfs/opt/speedtest-dashboard"
$elevate cp -R "$publish_dir/." "$rootfs/opt/speedtest-dashboard/"
$elevate install -m 0755 "$librespeed_dir/librespeed-cli" "$rootfs/opt/speedtest-dashboard/librespeed-cli"
$elevate install -m 0755 "$fastcom_dir/fast-cli" "$rootfs/opt/speedtest-dashboard/fast-cli"
$elevate install -d -m 0755 "$rootfs/opt/speedtest-dashboard/third-party-licenses"
$elevate install -m 0644 "$repo_root/LICENSE" "$rootfs/opt/speedtest-dashboard/LICENSE"
$elevate install -m 0644 "$repo_root/THIRD_PARTY_NOTICES.md" "$rootfs/opt/speedtest-dashboard/THIRD_PARTY_NOTICES.md"
$elevate install -m 0644 "$librespeed_dir/LICENSE.librespeed-cli" "$rootfs/opt/speedtest-dashboard/third-party-licenses/LICENSE.librespeed-cli"
$elevate install -m 0644 "$fastcom_dir/LICENSE.fast-cli" "$rootfs/opt/speedtest-dashboard/third-party-licenses/LICENSE.fast-cli"
if [ "$install_ookla" = true ]; then
  $elevate install -m 0644 "$repo_root/packaging/providers/OOKLA_NOTICE.md" "$rootfs/opt/speedtest-dashboard/third-party-licenses/OOKLA_NOTICE.md"
fi

$elevate install -d -m 0750 "$rootfs/etc/speedtest-dashboard"
$elevate install -m 0640 "$script_dir/environment" "$rootfs/etc/speedtest-dashboard/environment"
build_date=$(date -u +%Y-%m-%dT%H:%M:%SZ)
release_file="$work_dir/release"
printf 'version=%s\ncommit=%s\narchitecture=%s\nbuild_date=%s\nookla_cli=%s\n' \
  "$version" "$commit" "$architecture" "$build_date" "$install_ookla" > "$release_file"
$elevate install -m 0644 "$release_file" "$rootfs/etc/speedtest-dashboard/release"

$elevate install -d -o "$service_uid" -g "$service_gid" -m 0750 "$rootfs/var/lib/speedtest-dashboard"
$elevate install -d -o "$service_uid" -g "$service_gid" -m 0700 "$rootfs/var/lib/speedtest-dashboard/dataprotection"
$elevate install -m 0644 "$script_dir/speedtest-dashboard.service" "$rootfs/etc/systemd/system/speedtest-dashboard.service"
$elevate install -d -m 0755 "$rootfs/etc/systemd/system/multi-user.target.wants"
$elevate ln -s ../speedtest-dashboard.service "$rootfs/etc/systemd/system/multi-user.target.wants/speedtest-dashboard.service"

$elevate chown -R root:root "$rootfs/opt/speedtest-dashboard" "$rootfs/etc/speedtest-dashboard"
$elevate chmod 0755 "$rootfs/opt/speedtest-dashboard/SpeedtestDashboard.Api"
$elevate rm -f "$rootfs/usr/bin/$qemu_binary"
$elevate rm -rf "$rootfs/var/cache/apt/archives"/* "$rootfs/var/lib/apt/lists"/* "$rootfs/var/log"/*
$elevate truncate -s 0 "$rootfs/etc/machine-id"

$elevate tar \
  --create \
  --numeric-owner \
  --acls \
  --xattrs \
  --sort=name \
  --directory "$rootfs" \
  . | zstd -19 -T0 --force -o "$artifact"

(cd "$output" && sha256sum "$(basename "$artifact")") > "$artifact.sha256"
echo "$artifact"
