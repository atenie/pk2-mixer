#!/usr/bin/env bash
# Builds a macOS .app bundle from a published folder, ad-hoc signs it and
# produces both the .zip (ditto, preserves executable bits) and a .dmg.
# Must run on macOS.
#
# Usage: make-macos-bundle.sh <publishDir> <outputDir> <version> <arch> [icon.png]
set -euo pipefail

publish="${1:?usage: make-macos-bundle.sh <publishDir> <outputDir> <version> <arch> [icon.png]}"
out="${2:?}"
version="${3:?}"
arch="${4:?}"
icon="${5:-}"

if [ "$(uname -s)" != "Darwin" ]; then
    echo "error: macOS bundles must be built on macOS (codesign/ditto/hdiutil)" >&2
    exit 1
fi
if [ ! -d "$publish" ]; then
    echo "error: publish folder not found: $publish" >&2
    exit 1
fi

name="pk2-mixer"
app="$out/$name.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

# everything the self-contained publish produced lives next to the executable
cp -R "$publish"/. "$app/Contents/MacOS/"
if [ -f "$app/Contents/MacOS/Pk2Mixer" ]; then
    mv "$app/Contents/MacOS/Pk2Mixer" "$app/Contents/MacOS/$name"
fi
if [ ! -f "$app/Contents/MacOS/$name" ]; then
    echo "error: published executable not found (expected Pk2Mixer)" >&2
    exit 1
fi
chmod +x "$app/Contents/MacOS/$name"

icon_plist=""
if [ -n "$icon" ] && [ -f "$icon" ] && command -v sips >/dev/null && command -v iconutil >/dev/null; then
    iconset="$out/$name.iconset"
    rm -rf "$iconset"
    mkdir -p "$iconset"
    for size in 16 32 128 256 512; do
        sips -z "$size" "$size" "$icon" --out "$iconset/icon_${size}x${size}.png" >/dev/null
        sips -z "$((size * 2))" "$((size * 2))" "$icon" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
    done
    iconutil -c icns "$iconset" -o "$app/Contents/Resources/$name.icns"
    rm -rf "$iconset"
    icon_plist="<key>CFBundleIconFile</key><string>$name.icns</string>"
fi

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key><string>en</string>
    <key>CFBundleExecutable</key><string>$name</string>
    <key>CFBundleIdentifier</key><string>io.github.pk2mixer</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>CFBundleName</key><string>$name</string>
    <key>CFBundleDisplayName</key><string>pk2-mixer</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundleVersion</key><string>$version</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSHighResolutionCapable</key><true/>
    $icon_plist
</dict>
</plist>
PLIST

if command -v codesign >/dev/null; then
    # ad-hoc signature: required for arm64 binaries and avoids Gatekeeper
    # rejections for locally built artifacts; replace with a Developer ID
    # identity for public distribution.
    codesign --force --deep --sign - "$app"
fi

zip_path="$out/$name-$version-macos-$arch.zip"
rm -f "$zip_path"
ditto -c -k --sequesterRsrc --keepParent "$app" "$zip_path"
echo "wrote $zip_path"

if command -v hdiutil >/dev/null; then
    dmg_path="$out/$name-$version-macos-$arch.dmg"
    rm -f "$dmg_path"
    hdiutil create -volname "$name $version" -srcfolder "$app" -ov -format UDZO \
        "$dmg_path" >/dev/null
    echo "wrote $dmg_path"
fi
