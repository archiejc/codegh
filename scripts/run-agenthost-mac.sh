#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-/Users/jiachenboo/.dotnet/dotnet}"
CONFIGURATION="Debug"
SKIP_BUILD=0
BRIDGE_URI="${LIVECANVAS_BRIDGE_URI:-ws://127.0.0.1:17881/livecanvas/v0}"

usage() {
  cat <<'EOF'
Usage:
  scripts/run-agenthost-mac.sh [options]

Options:
  --configuration <Debug|Release>
  --bridge-uri <ws://...>
  --skip-build
  -h, --help

This script launches the stdio MCP server implemented by LiveCanvas.AgentHost.
Register this script as an MCP server command in your coding agent / IDE.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)
      CONFIGURATION="${2:-}"
      shift 2
      ;;
    --bridge-uri)
      BRIDGE_URI="${2:-}"
      shift 2
      ;;
    --skip-build)
      SKIP_BUILD=1
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

if [[ ! -x "${DOTNET_BIN}" ]]; then
  echo "dotnet executable not found at '${DOTNET_BIN}'." >&2
  exit 2
fi

if [[ ! "${BRIDGE_URI}" =~ ^ws:// ]]; then
  echo "Unsupported bridge URI '${BRIDGE_URI}'. Expected a ws:// URI." >&2
  exit 2
fi

PROJECT_PATH="${REPO_ROOT}/src/LiveCanvas.AgentHost/LiveCanvas.AgentHost.csproj"
DLL_PATH="${REPO_ROOT}/src/LiveCanvas.AgentHost/bin/${CONFIGURATION}/net8.0/LiveCanvas.AgentHost.dll"

if [[ "${SKIP_BUILD}" -eq 0 ]]; then
  echo "Building LiveCanvas.AgentHost (${CONFIGURATION})..." >&2
  "${DOTNET_BIN}" build "${PROJECT_PATH}" -c "${CONFIGURATION}" -v minimal >&2
fi

if [[ ! -f "${DLL_PATH}" ]]; then
  echo "LiveCanvas.AgentHost.dll not found at '${DLL_PATH}'." >&2
  echo "Run without --skip-build, or build the project first." >&2
  exit 2
fi

export LIVECANVAS_BRIDGE_URI="${BRIDGE_URI}"
exec "${DOTNET_BIN}" "${DLL_PATH}"
