#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIGURATION="Debug"
SKIP_OPEN=0
TIMEOUT_SECONDS="30"

usage() {
  cat <<'EOF'
Usage:
  scripts/dev-smoke-mac.sh [options]

Options:
  --configuration <Debug|Release>
  --timeout-seconds <int>
  --skip-open
  -h, --help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)
      CONFIGURATION="${2:-}"
      shift 2
      ;;
    --timeout-seconds)
      TIMEOUT_SECONDS="${2:-}"
      shift 2
      ;;
    --skip-open)
      SKIP_OPEN=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ "${CONFIGURATION}" != "Debug" && "${CONFIGURATION}" != "Release" ]]; then
  echo "Unsupported configuration '${CONFIGURATION}'. Expected Debug or Release." >&2
  exit 2
fi

if [[ ! "${TIMEOUT_SECONDS}" =~ ^[1-9][0-9]*$ ]]; then
  echo "--timeout-seconds must be a positive integer." >&2
  exit 2
fi

echo "Step 1/4: Build plugin and smoke harness..."
"${SCRIPT_DIR}/build-rhino-plugin-mac.sh" "${CONFIGURATION}"

echo "Step 2/4: Deploy plugin to Rhino MacPlugIns..."
"${SCRIPT_DIR}/deploy-rhino-plugin-mac.sh" "${CONFIGURATION}"

if [[ "${SKIP_OPEN}" -eq 0 ]]; then
  echo "Step 3/4: Open Rhino and Grasshopper..."
  "${SCRIPT_DIR}/open-rhino-grasshopper-mac.sh"
else
  echo "Step 3/4: Skipped opening Rhino/Grasshopper (--skip-open)."
fi

echo "Step 4/4: Run live smoke checks (bridge-only then full)..."
"${SCRIPT_DIR}/live-smoke-mac.sh" --configuration "${CONFIGURATION}" --timeout-seconds "${TIMEOUT_SECONDS}" --bridge-only
"${SCRIPT_DIR}/live-smoke-mac.sh" --configuration "${CONFIGURATION}" --timeout-seconds "${TIMEOUT_SECONDS}"

echo "dev-smoke completed successfully."
