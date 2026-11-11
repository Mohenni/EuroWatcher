#!/bin/sh
# EuroWatcher for Linux and macOS - installer.
#
#   Install:              sh install.sh
#   Remove:               sh install.sh --uninstall
#   Remove with my data:  sh install.sh --uninstall --purge
#
# Everything goes into your home folder; nothing is installed system-wide and nothing is compiled.
#   Linux : ~/.local/share/eurowatcher, the command ~/.local/bin/eurowatcher, and a menu entry
#   macOS : ~/Library/Application Support/EuroWatcher, the same command, and ~/Applications/EuroWatcher.app

set -eu

RAW="https://raw.githubusercontent.com/Mohenni/EuroWatcher/main"
FILES="unix/eurowatcher.py index.html styles.css app.js i18n.js data/draws.js assets/eurowatcher.png"
OS="$(uname -s)"
BIN_DIR="$HOME/.local/bin"
CMD="$BIN_DIR/eurowatcher"

if [ "$OS" = "Darwin" ]; then
    ROOT="$HOME/Library/Application Support/EuroWatcher"
    APP_BUNDLE="$HOME/Applications/EuroWatcher.app"
    DATA="$ROOT"
else
    ROOT="${XDG_DATA_HOME:-$HOME/.local/share}/eurowatcher"
    DESKTOP_FILE="${XDG_DATA_HOME:-$HOME/.local/share}/applications/eurowatcher.desktop"
    ICON_FILE="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/512x512/apps/eurowatcher.png"
    DATA="$ROOT/data"
fi
APP="$ROOT/app"

if [ "${1:-}" = "--uninstall" ]; then
    rm -rf "$APP"
    rm -f "$CMD"
    if [ "$OS" = "Darwin" ]; then
        rm -rf "$APP_BUNDLE"
    else
        rm -f "$DESKTOP_FILE" "$ICON_FILE"
        command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$(dirname "$DESKTOP_FILE")" >/dev/null 2>&1 || true
    fi
    if [ "${2:-}" = "--purge" ]; then
        rm -rf "$DATA"
        rmdir "$ROOT" 2>/dev/null || true
        echo "EuroWatcher and your saved data were removed."
    else
        rmdir "$ROOT" 2>/dev/null || true
        echo "EuroWatcher was removed. Your saved plays and draw data are still in $DATA"
        echo "(remove them too with: sh install.sh --uninstall --purge)"
    fi
    exit 0
fi

if [ "$OS" != "Linux" ] && [ "$OS" != "Darwin" ] && [ -z "${EUROWATCHER_SKIP_OS_CHECK:-}" ]; then
    echo "This installer is for Linux and macOS. On Windows, download EuroWatcherSetup.exe from"
    echo "https://github.com/Mohenni/EuroWatcher/releases"
    exit 1
fi

PYTHON=""
for candidate in python3 python; do
    if command -v "$candidate" >/dev/null 2>&1 &&
       "$candidate" -c 'import sys; sys.exit(0 if sys.version_info >= (3, 7) else 1)' 2>/dev/null; then
        PYTHON="$(command -v "$candidate")"
        break
    fi
done
if [ -z "$PYTHON" ]; then
    echo "EuroWatcher needs Python 3.7 or newer, which was not found."
    echo "  Debian/Ubuntu: sudo apt install python3      Fedora: sudo dnf install python3"
    echo "  macOS: install the Xcode command line tools with:  xcode-select --install"
    exit 1
fi

# Use the files next to this script when there are some (a downloaded or cloned project folder);
# otherwise fetch them from the project page.
HERE="$(cd "$(dirname "$0")" 2>/dev/null && pwd || echo .)"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
for f in $FILES; do
    mkdir -p "$STAGE/$(dirname "$f")"
    if [ -f "$HERE/unix/eurowatcher.py" ] && [ -f "$HERE/$f" ]; then
        cp "$HERE/$f" "$STAGE/$f"
    elif command -v curl >/dev/null 2>&1; then
        curl -fsSL "$RAW/$f" -o "$STAGE/$f"
    elif command -v wget >/dev/null 2>&1; then
        wget -q "$RAW/$f" -O "$STAGE/$f"
    else
        echo "Neither curl nor wget was found, so the program files could not be downloaded."
        exit 1
    fi
done

rm -rf "$APP"
mkdir -p "$APP/web/data" "$BIN_DIR"
cp "$STAGE/unix/eurowatcher.py" "$APP/eurowatcher.py"
cp "$STAGE/index.html" "$STAGE/styles.css" "$STAGE/app.js" "$STAGE/i18n.js" "$APP/web/"
cp "$STAGE/data/draws.js" "$APP/web/data/draws.js"
cp "$STAGE/assets/eurowatcher.png" "$APP/eurowatcher.png"

cat > "$CMD" <<EOF
#!/bin/sh
exec "$PYTHON" "$APP/eurowatcher.py" "\$@"
EOF
chmod +x "$CMD"

if [ "$OS" = "Darwin" ]; then
    # A small app in ~/Applications, so EuroWatcher opens from Launchpad, Spotlight or the Dock.
    rm -rf "$APP_BUNDLE"
    mkdir -p "$APP_BUNDLE/Contents/MacOS" "$APP_BUNDLE/Contents/Resources"
    cat > "$APP_BUNDLE/Contents/MacOS/EuroWatcher" <<EOF
#!/bin/sh
exec "$PYTHON" "$APP/eurowatcher.py" >/dev/null 2>&1
EOF
    chmod +x "$APP_BUNDLE/Contents/MacOS/EuroWatcher"
    cat > "$APP_BUNDLE/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>EuroWatcher</string>
  <key>CFBundleDisplayName</key><string>EuroWatcher</string>
  <key>CFBundleIdentifier</key><string>io.github.mohenni.eurowatcher</string>
  <key>CFBundleVersion</key><string>1.0.0</string>
  <key>CFBundleShortVersionString</key><string>1.0.0</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>EuroWatcher</string>
  <key>CFBundleIconFile</key><string>eurowatcher</string>
  <key>LSUIElement</key><true/>
</dict>
</plist>
EOF
    # The icon: macOS wants an .icns file, made here from the PNG with the tools macOS includes.
    if command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
        SET="$STAGE/eurowatcher.iconset"
        mkdir -p "$SET"
        for s in 16 32 128 256; do
            sips -z $s $s "$APP/eurowatcher.png" --out "$SET/icon_${s}x${s}.png" >/dev/null 2>&1 || true
            d=$((s * 2))
            sips -z $d $d "$APP/eurowatcher.png" --out "$SET/icon_${s}x${s}@2x.png" >/dev/null 2>&1 || true
        done
        iconutil -c icns "$SET" -o "$APP_BUNDLE/Contents/Resources/eurowatcher.icns" >/dev/null 2>&1 || true
    fi
    touch "$APP_BUNDLE"
else
    # A menu entry, so EuroWatcher appears with the other applications.
    mkdir -p "$(dirname "$DESKTOP_FILE")" "$(dirname "$ICON_FILE")"
    cp "$APP/eurowatcher.png" "$ICON_FILE"
    cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Name=EuroWatcher
GenericName=EuroMillions results
Comment=Every EuroMillions draw, checked against your numbers
Exec="$CMD"
Icon=eurowatcher
Terminal=false
Categories=Utility;
EOF
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$(dirname "$DESKTOP_FILE")" >/dev/null 2>&1 || true
fi

"$PYTHON" "$APP/eurowatcher.py" --selftest >/dev/null && echo "EuroWatcher is installed." || echo "Installed, but its self-test failed. Please report this."
echo
if [ "$OS" = "Darwin" ]; then
    echo "Open it from Launchpad or Spotlight (EuroWatcher), or with the command:  eurowatcher"
else
    echo "Open it from your applications menu (EuroWatcher), or with the command:  eurowatcher"
fi
case ":$PATH:" in
    *":$BIN_DIR:"*) ;;
    *) echo "(Your PATH does not contain $BIN_DIR yet. Start it with:  $CMD)" ;;
esac
echo "It opens in its own window when Chrome, Chromium, Edge or Brave is installed, otherwise in your usual browser."
echo "Remove it with:  sh install.sh --uninstall"
