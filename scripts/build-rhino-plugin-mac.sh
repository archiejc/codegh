#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CONFIGURATION="${1:-Debug}"

resolve_dotnet() {
  if [[ -n "${DOTNET_BIN:-}" ]]; then
    if [[ -x "${DOTNET_BIN}" ]]; then
      echo "${DOTNET_BIN}"
      return
    fi
    echo "DOTNET_BIN is set but not executable: '${DOTNET_BIN}'." >&2
    exit 2
  fi

  if [[ -x "${HOME}/.dotnet/dotnet" ]]; then
    echo "${HOME}/.dotnet/dotnet"
    return
  fi

  if command -v dotnet >/dev/null 2>&1; then
    command -v dotnet
    return
  fi

  echo "dotnet executable not found. Set DOTNET_BIN or install dotnet into PATH." >&2
  exit 2
}

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

DOTNET_BIN="$(resolve_dotnet)"

echo "Building Rhino plugin (net7.0, ${CONFIGURATION})..."
"${DOTNET_BIN}" build "${REPO_ROOT}/src/LiveCanvas.RhinoPlugin/LiveCanvas.RhinoPlugin.csproj" -c "${CONFIGURATION}" -f net7.0 -v minimal

echo "Building AgentHost (${CONFIGURATION})..."
"${DOTNET_BIN}" build "${REPO_ROOT}/src/LiveCanvas.AgentHost/LiveCanvas.AgentHost.csproj" -c "${CONFIGURATION}" -v minimal

echo "Building smoke harness (${CONFIGURATION})..."
"${DOTNET_BIN}" build "${REPO_ROOT}/tools/LiveCanvas.SmokeHarness/LiveCanvas.SmokeHarness.csproj" -c "${CONFIGURATION}" -v minimal

echo "Build complete."
