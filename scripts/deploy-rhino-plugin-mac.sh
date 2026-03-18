#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CONFIGURATION="${1:-Debug}"

if [[ "${CONFIGURATION}" == "-h" || "${CONFIGURATION}" == "--help" ]]; then
  cat <<'EOF'
Usage:
  scripts/deploy-rhino-plugin-mac.sh [Debug|Release]
EOF
  exit 0
fi

if [[ "${CONFIGURATION}" != "Debug" && "${CONFIGURATION}" != "Release" ]]; then
  echo "Unsupported configuration '${CONFIGURATION}'. Expected Debug or Release." >&2
  exit 2
fi

SOURCE_DIR="${REPO_ROOT}/src/LiveCanvas.RhinoPlugin/bin/${CONFIGURATION}/net7.0"
DEST_ROOT="${HOME}/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns"
DEST_DIR_A="${DEST_ROOT}/LiveCanvas.RhinoPlugin"
DEST_DIR_B="${DEST_ROOT}/LiveCanvas.RhinoPlugin.rhp"

if [[ ! -f "${SOURCE_DIR}/LiveCanvas.RhinoPlugin.rhp" ]]; then
  echo "Built plugin not found at '${SOURCE_DIR}/LiveCanvas.RhinoPlugin.rhp'." >&2
  echo "Run scripts/build-rhino-plugin-mac.sh ${CONFIGURATION} first." >&2
  exit 2
fi

mkdir -p "${DEST_DIR_A}" "${DEST_DIR_B}"

echo "Syncing plugin files to '${DEST_DIR_A}'..."
rsync -a --delete "${SOURCE_DIR}/" "${DEST_DIR_A}/"

echo "Syncing plugin files to '${DEST_DIR_B}'..."
rsync -a --delete "${SOURCE_DIR}/" "${DEST_DIR_B}/"

if [[ -f "${DEST_DIR_A}/LiveCanvas.RhinoPlugin.runtimeconfig.json" ]]; then
  echo "Deployed runtimeconfig:"
  cat "${DEST_DIR_A}/LiveCanvas.RhinoPlugin.runtimeconfig.json"
  echo
fi

echo "Deploy complete."
