#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TARGET="both"
CONFIGURATION="Debug"
SKIP_BUILD_DEPLOY=0
CODEX_CONFIG="${HOME}/.codex/config.toml"
CLAUDE_CONFIG="${HOME}/.claude.json"
RUNNER_SCRIPT="${REPO_ROOT}/scripts/run-agenthost-mac.sh"
BUILD_SCRIPT="${REPO_ROOT}/scripts/build-rhino-plugin-mac.sh"
DEPLOY_SCRIPT="${REPO_ROOT}/scripts/deploy-rhino-plugin-mac.sh"

usage() {
  cat <<'EOF'
Usage:
  scripts/install-mcp-livecanvas-mac.sh [options]

Options:
  --target <codex|claude|both>    Which client config(s) to update (default: both)
  --configuration <Debug|Release> Build configuration to deploy (default: Debug)
  --skip-build-deploy             Only update MCP client config, skip local build/deploy
  --codex-config <path>           Override Codex config path (default: ~/.codex/config.toml)
  --claude-config <path>          Override Claude config path (default: ~/.claude.json)
  -h, --help

This script optionally builds and deploys the Rhino plugin, then writes a
livecanvas stdio MCP entry using:
  /bin/bash <repo>/scripts/run-agenthost-mac.sh --skip-build
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --target)
      TARGET="${2:-}"
      shift 2
      ;;
    --configuration)
      CONFIGURATION="${2:-}"
      shift 2
      ;;
    --skip-build-deploy)
      SKIP_BUILD_DEPLOY=1
      shift
      ;;
    --codex-config)
      CODEX_CONFIG="${2:-}"
      shift 2
      ;;
    --claude-config)
      CLAUDE_CONFIG="${2:-}"
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

if [[ "${TARGET}" != "codex" && "${TARGET}" != "claude" && "${TARGET}" != "both" ]]; then
  echo "Unsupported target '${TARGET}'. Expected codex, claude, or both." >&2
  exit 2
fi

if [[ "${CONFIGURATION}" != "Debug" && "${CONFIGURATION}" != "Release" ]]; then
  echo "Unsupported configuration '${CONFIGURATION}'. Expected Debug or Release." >&2
  exit 2
fi

if [[ ! -f "${RUNNER_SCRIPT}" ]]; then
  echo "Missing runner script: '${RUNNER_SCRIPT}'." >&2
  exit 2
fi

if [[ ! -f "${BUILD_SCRIPT}" || ! -f "${DEPLOY_SCRIPT}" ]]; then
  echo "Missing build/deploy helper scripts under '${REPO_ROOT}/scripts'." >&2
  exit 2
fi

chmod +x "${RUNNER_SCRIPT}"
chmod +x "${BUILD_SCRIPT}"
chmod +x "${DEPLOY_SCRIPT}"

timestamp="$(date +%Y%m%d%H%M%S)"

if [[ "${SKIP_BUILD_DEPLOY}" -eq 0 ]]; then
  echo "Building local LiveCanvas artifacts (${CONFIGURATION})..."
  bash "${BUILD_SCRIPT}" "${CONFIGURATION}"

  echo "Deploying Rhino plugin to MacPlugIns..."
  bash "${DEPLOY_SCRIPT}" "${CONFIGURATION}"
else
  echo "Skipping build/deploy and updating client config only."
fi

update_codex() {
  mkdir -p "$(dirname "${CODEX_CONFIG}")"
  if [[ -f "${CODEX_CONFIG}" ]]; then
    cp "${CODEX_CONFIG}" "${CODEX_CONFIG}.bak.${timestamp}"
  fi

  python3 - "${CODEX_CONFIG}" "${RUNNER_SCRIPT}" <<'PY'
import pathlib
import re
import sys

config_path = pathlib.Path(sys.argv[1]).expanduser()
runner_script = sys.argv[2]
table_header = re.compile(r"^\[mcp_servers\.livecanvas\]\s*$")
any_table = re.compile(r"^\[.*\]\s*$")

if config_path.exists():
    lines = config_path.read_text(encoding="utf-8").splitlines(keepends=True)
else:
    lines = []

out = []
i = 0
while i < len(lines):
    stripped = lines[i].strip()
    if table_header.match(stripped):
        i += 1
        while i < len(lines) and not any_table.match(lines[i].strip()):
            i += 1
        while i < len(lines) and lines[i].strip() == "":
            i += 1
        continue
    out.append(lines[i])
    i += 1

text = "".join(out).rstrip()
if text:
    text += "\n\n"
text += "[mcp_servers.livecanvas]\n"
text += 'command = "/bin/bash"\n'
text += f'args = [ "{runner_script}", "--skip-build" ]\n'
text += "\n"

config_path.write_text(text, encoding="utf-8")
PY

  echo "Updated Codex MCP config: ${CODEX_CONFIG}"
}

update_claude() {
  mkdir -p "$(dirname "${CLAUDE_CONFIG}")"
  if [[ -f "${CLAUDE_CONFIG}" ]]; then
    cp "${CLAUDE_CONFIG}" "${CLAUDE_CONFIG}.bak.${timestamp}"
  fi

  python3 - "${CLAUDE_CONFIG}" "${RUNNER_SCRIPT}" <<'PY'
import json
import pathlib
import sys

config_path = pathlib.Path(sys.argv[1]).expanduser()
runner_script = sys.argv[2]

if config_path.exists():
    with config_path.open("r", encoding="utf-8") as f:
        data = json.load(f)
else:
    data = {}

if not isinstance(data.get("mcpServers"), dict):
    data["mcpServers"] = {}

data["mcpServers"]["livecanvas"] = {
    "command": "/bin/bash",
    "args": [runner_script, "--skip-build"],
    "type": "stdio"
}

with config_path.open("w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
    f.write("\n")
PY

  echo "Updated Claude MCP config: ${CLAUDE_CONFIG}"
}

case "${TARGET}" in
  codex)
    update_codex
    ;;
  claude)
    update_claude
    ;;
  both)
    update_codex
    update_claude
    ;;
esac

echo
echo "Install complete. Restart Codex / Claude clients to reload MCP servers."
echo "Configured command:"
echo "  /bin/bash ${RUNNER_SCRIPT} --skip-build"
