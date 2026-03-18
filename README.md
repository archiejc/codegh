# LiveCanvas (Rhino 8 + Grasshopper local bridge)

This repository contains a local LiveCanvas runtime stack for Rhino 8 and Grasshopper.
It is designed for an agent-driven workflow where commands flow through MCP stdio into a Rhino-side WebSocket bridge and mutate a real Grasshopper document.

Current baseline on this machine:
- `live` smoke harness is passing against Rhino 8 for macOS
- plugin runtime on macOS is deployed as `net7.0`

## What is in this repo

Core projects:
- `src/LiveCanvas.RhinoPlugin`:
  Rhino plugin that hosts the local bridge (`ws://127.0.0.1:17881/livecanvas/v0`)
- `src/LiveCanvas.AgentHost`:
  MCP stdio host that exposes `gh_*` tools and talks to the Rhino bridge
- `src/LiveCanvas.Bridge.Protocol`:
  JSON-RPC method names and serialization
- `src/LiveCanvas.Contracts`:
  request/response contracts
- `src/LiveCanvas.Core`:
  allowed component registry and validation logic
- `tools/LiveCanvas.SmokeHarness`:
  dual-mode smoke harness (`mock` and `live`)

Tests:
- `tests/LiveCanvas.*.Tests`

## Runtime architecture

```text
Agent / CLI
  -> MCP stdio
  -> LiveCanvas.AgentHost
  -> ws://127.0.0.1:17881/livecanvas/v0
  -> LiveCanvas.RhinoPlugin (inside Rhino 8)
  -> Grasshopper document mutations, solve, inspect, capture, save
```

## Prerequisites (macOS)

- Rhino 8 installed at `/Applications/Rhino 8.app`
- Grasshopper available in Rhino
- .NET SDK executable at `/Users/jiachenboo/.dotnet/dotnet`
- Terminal has Accessibility permission if you want scripted UI keystrokes (for opening new document + Grasshopper)

Optional but recommended:
- keep Rhino and Grasshopper visible while running live smoke so you can observe graph changes in-session

## Build from source

From repo root:

```bash
/Users/jiachenboo/.dotnet/dotnet build LiveCanvas.sln -v minimal
```

Or use the provided helper:

```bash
scripts/build-rhino-plugin-mac.sh Debug
```

To build just the MCP stdio server that a coding agent will talk to:

```bash
/Users/jiachenboo/.dotnet/dotnet build src/LiveCanvas.AgentHost/LiveCanvas.AgentHost.csproj -c Debug -v minimal
```

## Deploy plugin to Rhino MacPlugIns

Build first, then deploy `net7.0` plugin output into both plugin directories:

```bash
scripts/deploy-rhino-plugin-mac.sh Debug
```

This syncs:
- `~/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/LiveCanvas.RhinoPlugin/`
- `~/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/LiveCanvas.RhinoPlugin.rhp/`

## Open Rhino and Grasshopper

Automated helper (requires Accessibility permission):

```bash
scripts/open-rhino-grasshopper-mac.sh
```

Manual alternative:
1. Open Rhino 8
2. Create a new document
3. Run `Grasshopper` command

## Run live smoke harness

Bridge preflight only:

```bash
scripts/live-smoke-mac.sh --bridge-only --timeout-seconds 30
```

Full live chain:

```bash
scripts/live-smoke-mac.sh --timeout-seconds 30
```

Direct command without helper script:

```bash
/Users/jiachenboo/.dotnet/dotnet run --project tools/LiveCanvas.SmokeHarness/LiveCanvas.SmokeHarness.csproj -- --mode live --live-preflight-timeout-seconds 30
```

One-shot dev flow (build + deploy + open + bridge-only + full):

```bash
scripts/dev-smoke-mac.sh --timeout-seconds 30
```

If Rhino and Grasshopper are already open:

```bash
scripts/dev-smoke-mac.sh --skip-open --timeout-seconds 30
```

## Use from a coding agent / IDE

The repo-side architecture is already set up for an MCP-capable coding agent:

```text
Coding agent / IDE
  -> stdio MCP server
  -> scripts/run-agenthost-mac.sh
  -> LiveCanvas.AgentHost
  -> LiveCanvas.RhinoPlugin bridge
  -> live Grasshopper / Rhino updates
```

Start by making sure Rhino 8 is running, the plugin is deployed, and Grasshopper is open.

Then register this command as an MCP stdio server in your coding agent / IDE:

```bash
/Users/jiachenboo/Research/codegh/scripts/run-agenthost-mac.sh --skip-build
```

If you want the launcher to rebuild `LiveCanvas.AgentHost` automatically before startup:

```bash
/Users/jiachenboo/Research/codegh/scripts/run-agenthost-mac.sh
```

If your client needs an explicit bridge URI:

```bash
/Users/jiachenboo/Research/codegh/scripts/run-agenthost-mac.sh --skip-build --bridge-uri ws://127.0.0.1:17881/livecanvas/v0
```

Exact local client snippets on this machine:

Codex (`~/.codex/config.toml`):

```toml
[mcp_servers.livecanvas]
command = "/bin/bash"
args = ["/Users/jiachenboo/Research/codegh/scripts/run-agenthost-mac.sh", "--skip-build"]
```

Claude Code (`~/.claude.json` inside top-level `mcpServers`):

```json
"livecanvas": {
  "command": "/bin/bash",
  "args": [
    "/Users/jiachenboo/Research/codegh/scripts/run-agenthost-mac.sh",
    "--skip-build"
  ],
  "env": {},
  "type": "stdio"
}
```

After adding the MCP server entry, restart the client so it reloads the MCP server list.

Once connected, the coding agent can call the exposed `gh_*` tools to:
- inspect session state
- create a Grasshopper document
- add and configure components
- connect, solve, inspect, capture, and save

If you tell me which host you want to use first, I can give you the exact MCP config snippet for that client.

## Run a modeling validation scene

To prove the live MCP path is doing more than the baseline extrusion smoke test, the repo also includes an Absolute Towers / "Marilyn Monroe towers" style demo scene.

With Rhino 8 running, an active Rhino document open, and Grasshopper visible:

```bash
bash scripts/demo-absolute-towers-mac.sh --timeout-seconds 30
```

This drives the same `LiveCanvas.AgentHost` MCP tool surface and builds a twisting rounded-floor-plate tower by:
- creating several rounded rectangle floor profiles
- moving them upward at increasing heights
- rotating upper floors around the world Z axis
- lofting the profiles into a single tapered tower mass

Expected stdout includes:
- `[ok] bridge-jsonrpc-live`
- `[ok] mcp-stdio-live`
- `output_dir=...`

Inside the printed `output_dir`, look for:
- `absolute-towers.gh`
- `manifest.json`
- `transcript.json`

If Rhino currently exposes an active document and viewport, the run will also write:
- `preview.png`

If Rhino drops the active document during automated capture, the harness still treats the modeling run as successful, saves `absolute-towers.gh`, and records a `capture_skipped` warning in `manifest.json`.

## Expected success signals

For full live smoke, expected stdout includes:
- `[ok] bridge-jsonrpc-live`
- `[ok] mcp-stdio-live`
- `output_dir=...`

In the printed `output_dir`, expected artifacts:
- `manifest.json`
- `transcript.json`
- `preview.png`
- `smoke.gh`

`manifest.json` should include:
- `"success": true`
- `"completedChecks": ["bridge-jsonrpc-live", "mcp-stdio-live"]`

## Troubleshooting

### `bridge_unreachable` or preflight timeout

- Rhino 8 is not running, plugin not loaded, or bridge not listening
- Re-deploy plugin, restart Rhino, open Grasshopper, retry
- Verify default URI is `ws://127.0.0.1:17881/livecanvas/v0`

### `live_precondition_failed` in `gh_session_info`

- Grasshopper was not loaded in the active Rhino session
- Open Grasshopper and retry

### `Could not resolve builtin component ...`

- Plugin deployment and runtime mismatch (stale plugin folder or wrong target framework)
- Re-run:
  - `scripts/build-rhino-plugin-mac.sh Debug`
  - `scripts/deploy-rhino-plugin-mac.sh Debug`
- Restart Rhino and retry live smoke

### `artifact_missing` after `gh_inspect_document`

- The graph solved but no valid preview geometry was detected
- Check `transcript.json` and plugin diagnostic log for component/runtime details

### Terminal cannot drive Rhino UI

- Grant Accessibility permissions for Terminal (System Settings -> Privacy & Security -> Accessibility)
- Or open Rhino/Grasshopper manually and run smoke scripts with `--skip-open`
- If `scripts/open-rhino-grasshopper-mac.sh` appears to hang, stop it and continue with manual Rhino/Grasshopper opening

### Diagnostic log location

Plugin diagnostics are written under the macOS temp directory as:
- `<TMPDIR>/livecanvas-rhino-plugin.log`

Example on this machine:
- `/var/folders/.../T/livecanvas-rhino-plugin.log`

## Helper scripts summary

- `scripts/build-rhino-plugin-mac.sh [Debug|Release]`
- `scripts/deploy-rhino-plugin-mac.sh [Debug|Release]`
- `scripts/run-agenthost-mac.sh [--configuration Debug|Release] [--skip-build] [--bridge-uri ws://...]`
- `scripts/demo-absolute-towers-mac.sh [--timeout-seconds N] [--output-dir PATH] [--skip-build-agent-host]`
- `scripts/open-rhino-grasshopper-mac.sh`
- `scripts/live-smoke-mac.sh [--bridge-only|--mcp-only] [--timeout-seconds N] [--output-dir PATH] [--bridge-uri ws://...]`
- `scripts/dev-smoke-mac.sh [--skip-open] [--timeout-seconds N] [--configuration Debug|Release]`

## Notes

- This repo currently has no automatic Rhino launch/install in CI; `live` smoke is an attach-to-running-session flow.
- `tmp/` is ignored on purpose for local live verification artifacts.
