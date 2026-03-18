#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-/Users/jiachenboo/.dotnet/dotnet}"
CONFIGURATION="${1:-Debug}"

if [[ "${CONFIGURATION}" == "-h" || "${CONFIGURATION}" == "--help" ]]; then
  cat <<'EOF'
Usage:
  scripts/build-rhino-plugin-mac.sh [Debug|Release]
EOF
  exit 0
fi

if [[ "${CONFIGURATION}" != "Debug" && "${CONFIGURATION}" != "Release" ]]; then
  echo "Unsupported configuration '${CONFIGURATION}'. Expected Debug or Release." >&2
  exit 2
fi

if [[ ! -x "${DOTNET_BIN}" ]]; then
  echo "dotnet executable not found at '${DOTNET_BIN}'." >&2
  echo "Set DOTNET_BIN to your dotnet path, or install dotnet at /Users/jiachenboo/.dotnet/dotnet." >&2
  exit 2
fi

echo "Building Rhino plugin (net7.0, ${CONFIGURATION})..."
"${DOTNET_BIN}" build "${REPO_ROOT}/src/LiveCanvas.RhinoPlugin/LiveCanvas.RhinoPlugin.csproj" -c "${CONFIGURATION}" -f net7.0 -v minimal

echo "Building smoke harness (${CONFIGURATION})..."
"${DOTNET_BIN}" build "${REPO_ROOT}/tools/LiveCanvas.SmokeHarness/LiveCanvas.SmokeHarness.csproj" -c "${CONFIGURATION}" -v minimal

echo "Build complete."
