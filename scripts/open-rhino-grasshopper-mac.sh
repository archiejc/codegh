#!/usr/bin/env bash
set -euo pipefail

RHINO_APP="${RHINO_APP:-/Applications/Rhino 8.app}"

if [[ ! -d "${RHINO_APP}" ]]; then
  echo "Rhino app not found at '${RHINO_APP}'." >&2
  echo "Set RHINO_APP to your Rhino 8.app location." >&2
  exit 2
fi

echo "Opening Rhino..."
open -a "${RHINO_APP}"

echo "Waiting for Rhino UI to initialize..."
sleep 8

echo "Opening a new document and Grasshopper (requires Accessibility permission for Terminal)..."
osascript <<'APPLESCRIPT'
tell application "Rhino 8" to activate
delay 1
tell application "System Events"
    keystroke "n" using command down
    delay 1
    key code 36
    delay 4
    keystroke "_Grasshopper"
    key code 36
end tell
APPLESCRIPT

echo "Rhino and Grasshopper should now be open."
