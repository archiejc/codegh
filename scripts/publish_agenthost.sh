#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_PATH="${REPO_ROOT}/src/LiveCanvas.AgentHost/LiveCanvas.AgentHost.csproj"

CONFIGURATION="Release"
OUTPUT_DIR="${REPO_ROOT}/dist/agenthost"

usage() {
  cat <<EOF
Usage: $(basename "$0") [--configuration <Release|Debug>] [--output <path>]

Publishes LiveCanvas.AgentHost to a local output directory.

Defaults:
  --configuration Release
  --output        ${REPO_ROOT}/dist/agenthost
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    --configuration|-c)
      CONFIGURATION="${2:-}"
      shift 2
      ;;
    --output|-o)
      OUTPUT_DIR="${2:-}"
      shift 2
      ;;
    *)
      echo "[publish_agenthost] Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -z "${CONFIGURATION}" || -z "${OUTPUT_DIR}" ]]; then
  echo "[publish_agenthost] --configuration and --output values must not be empty." >&2
  exit 1
fi

echo "[publish_agenthost] Project: ${PROJECT_PATH}"
echo "[publish_agenthost] Configuration: ${CONFIGURATION}"
echo "[publish_agenthost] Output: ${OUTPUT_DIR}"

dotnet publish "${PROJECT_PATH}" -c "${CONFIGURATION}" -o "${OUTPUT_DIR}"

echo
echo "[publish_agenthost] Done."
echo "[publish_agenthost] Next:"
echo "  python3 ${REPO_ROOT}/scripts/smoke_mcp_stdio.py --agent-host ${OUTPUT_DIR}"
