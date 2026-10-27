#!/usr/bin/env bash
# Builds an AppImage from a prepared AppDir.
#
# Runs on Linux (or inside WSL). appimagetool is downloaded to the user cache
# when it is not already available, and run with --appimage-extract-and-run so
# no FUSE mount is required on build hosts/containers.
#
# Usage: make-appimage.sh <AppDir> <output.AppImage>
set -euo pipefail

appdir="${1:?usage: make-appimage.sh <AppDir> <output.AppImage>}"
output="${2:?usage: make-appimage.sh <AppDir> <output.AppImage>}"

if [ ! -d "$appdir" ]; then
    echo "error: AppDir not found: $appdir" >&2
    exit 1
fi
if [ ! -x "$appdir/AppRun" ] && [ ! -f "$appdir/AppRun" ]; then
    echo "error: AppDir has no AppRun: $appdir" >&2
    exit 1
fi

mkdir -p "$(dirname "$output")"

machine="$(uname -m)"
case "$machine" in
    x86_64|amd64) appimage_arch="x86_64" ;;
    aarch64|arm64) appimage_arch="aarch64" ;;
    *)
        echo "error: unsupported architecture: $machine" >&2
        exit 1
        ;;
esac
export ARCH="$appimage_arch"

chmod +x "$appdir/AppRun"
if [ -f "$appdir/usr/bin/pk2-mixer" ]; then
    chmod +x "$appdir/usr/bin/pk2-mixer"
fi

# Build from a Linux-side copy with normalised modes: AppDirs prepared on a
# Windows drive (WSL /mnt/c) otherwise carry 0777 for every file.
workdir="$(mktemp -d "${TMPDIR:-/tmp}/pk2-mixer-appdir.XXXXXX")"
trap 'rm -rf "$workdir"' EXIT
cp -a "$appdir/." "$workdir/"
find "$workdir" -type d -exec chmod 755 {} +
find "$workdir" -type f -exec chmod 644 {} +
chmod 755 "$workdir/AppRun"
if [ -f "$workdir/usr/bin/pk2-mixer" ]; then
    chmod 755 "$workdir/usr/bin/pk2-mixer"
fi

tool="${APPIMAGETOOL:-}"
if [ -z "$tool" ]; then
    cache="${XDG_CACHE_HOME:-$HOME/.cache}/pk2-mixer"
    mkdir -p "$cache"
    tool="$cache/appimagetool-$appimage_arch.AppImage"
    if [ ! -x "$tool" ]; then
        echo "downloading appimagetool ($appimage_arch)…"
        curl -fL --retry 3 --connect-timeout 20 --max-time 300 \
            -o "$tool" \
            "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$appimage_arch.AppImage"
        chmod +x "$tool"
    fi
fi

rm -f "$output"
"$tool" --appimage-extract-and-run --no-appstream "$workdir" "$output"
chmod +x "$output"
echo "wrote $output"
