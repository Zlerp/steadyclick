#!/bin/bash
# Builds SteadyClick.app (Apple silicon + Intel) and zips it. Needs only Apple's Command Line Tools:
#   xcode-select --install
# Usage: bash mac/build.sh
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${STEADYCLICK_VERSION:-1.0.0}"
APP="build/SteadyClick.app"
SDK="$(xcrun --sdk macosx --show-sdk-path)"

rm -rf build
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

for ARCH in arm64 x86_64; do
  echo "Compiling for $ARCH..."
  xcrun swiftc -O -swift-version 5 \
    -target "$ARCH-apple-macos13.0" -sdk "$SDK" \
    -o "build/SteadyClick-$ARCH" Sources/*.swift
done
lipo -create build/SteadyClick-arm64 build/SteadyClick-x86_64 -output "$APP/Contents/MacOS/SteadyClick"
rm build/SteadyClick-arm64 build/SteadyClick-x86_64

# App icon: one 1024 px PNG scaled into the sizes macOS wants.
if [ -f icon-1024.png ]; then
  ICONSET="build/AppIcon.iconset"
  mkdir -p "$ICONSET"
  for SIZE in 16 32 128 256 512; do
    sips -z "$SIZE" "$SIZE" icon-1024.png --out "$ICONSET/icon_${SIZE}x${SIZE}.png" >/dev/null
    DOUBLE=$((SIZE * 2))
    sips -z "$DOUBLE" "$DOUBLE" icon-1024.png --out "$ICONSET/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
  rm -rf "$ICONSET"
fi

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>SteadyClick</string>
  <key>CFBundleDisplayName</key><string>SteadyClick</string>
  <key>CFBundleIdentifier</key><string>com.zlerp.steadyclick</string>
  <key>CFBundleExecutable</key><string>SteadyClick</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST

# Ad-hoc signature: enough for the app to run and to hold its Accessibility permission.
codesign --force --deep --sign - "$APP"

(cd build && ditto -c -k --keepParent SteadyClick.app SteadyClick-mac.zip)
echo "Built $(pwd)/$APP"
echo "Zipped $(pwd)/build/SteadyClick-mac.zip"
