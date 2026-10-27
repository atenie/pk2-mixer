#!/usr/bin/env bash
# Convenience entry point for Linux/macOS: forwards to the PowerShell
# build script, which drives the native bits (appimagetool, codesign, ditto,
# hdiutil) itself. Requires PowerShell 7+ (pwsh).
#
# Usage: ./tools/dist.sh [-Targets linux|macos|all] [-Version X.Y.Z]
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"

if command -v pwsh >/dev/null 2>&1; then
    exec pwsh -NoProfile -File "$here/dist.ps1" "$@"
fi

echo "error: PowerShell 7 (pwsh) is required for tools/dist.ps1." >&2
echo "       Install it from https://github.com/PowerShell/PowerShell" >&2
echo "       or run the native helpers directly:" >&2
echo "         tools/make-appimage.sh <AppDir> <output.AppImage>" >&2
echo "         tools/make-macos-bundle.sh <publishDir> <outDir> <version> <arch> [icon.png]" >&2
exit 1
