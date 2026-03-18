#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-/Users/jiachenboo/.dotnet/dotnet}"
CONFIGURATION="Debug"
TIMEOUT_SECONDS="30"
CHECK_MODE=""
OUTPUT_DIR=""
BRIDGE_URI=""

usage() {
  cat <<'EOF'
Usage:
  scripts/live-smoke-mac.sh [options]

Options:
  --configuration <Debug|Release>
  --timeout-seconds <int>
  --bridge-only
  --mcp-only
  --output-dir <path>
  --bridge-uri <ws://...>
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
    --bridge-only)
      CHECK_MODE="--bridge-only"
      shift
      ;;
    --mcp-only)
      CHECK_MODE="--mcp-only"
      shift
      ;;
    --output-dir)
      OUTPUT_DIR="${2:-}"
      shift 2
      ;;
    --bridge-uri)
      BRIDGE_URI="${2:-}"
      shift 2
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

if [[ ! -x "${DOTNET_BIN}" ]]; then
  echo "dotnet executable not found at '${DOTNET_BIN}'." >&2
  exit 2
fi

CMD=(
  "${DOTNET_BIN}" run
  --project "${REPO_ROOT}/tools/LiveCanvas.SmokeHarness/LiveCanvas.SmokeHarness.csproj"
  --configuration "${CONFIGURATION}"
  --
  --mode live
  --live-preflight-timeout-seconds "${TIMEOUT_SECONDS}"
)

if [[ -n "${CHECK_MODE}" ]]; then
  CMD+=("${CHECK_MODE}")
fi

if [[ -n "${OUTPUT_DIR}" ]]; then
  CMD+=(--output-dir "${OUTPUT_DIR}")
fi

if [[ -n "${BRIDGE_URI}" ]]; then
  CMD+=(--bridge-uri "${BRIDGE_URI}")
fi

echo "Running live smoke harness..."
"${CMD[@]}"
