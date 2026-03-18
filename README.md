# LiveCanvas MCP for Rhino + Grasshopper

This repository contains a `.NET` MCP host and a Rhino plugin that together provide a local Grasshopper automation workflow.

The MCP entrypoint is `src/LiveCanvas.AgentHost` (stdio MCP server).

The legacy folder `mcps/GH_mcp_server` is currently empty and is not the runtime entrypoint for this repository.

## What Is In This Repo

- `src/LiveCanvas.AgentHost`: stdio MCP server (`initialize`, `tools/list`, `tools/call`)
- `src/LiveCanvas.RhinoPlugin`: Rhino plugin (`.rhp`) exposing a local websocket bridge
- `src/LiveCanvas.Bridge.Protocol`: shared bridge protocol/constants
- `src/LiveCanvas.Contracts` and `src/LiveCanvas.Core`: tool contracts and validation/planning logic
- `tests/*`: unit and integration test projects (see limitations section for current test-source gaps)

## Architecture

1. MCP client (Codex/Claude/Cursor/etc.) starts `LiveCanvas.AgentHost` over stdio.
2. `LiveCanvas.AgentHost` exposes MCP tools such as `gh_session_info`, `gh_add_component`, `gh_save_document`.
3. Tool handlers call a local websocket bridge (default `ws://127.0.0.1:17881/livecanvas/v0`).
4. `LiveCanvas.RhinoPlugin` runs inside Rhino, receives bridge requests, and manipulates Grasshopper.

## Prerequisites

### Common

- `.NET SDK 8.x` (`dotnet --info`)
- Git
- Python 3 (for helper smoke scripts under `scripts/`)

### macOS

- Rhino 8 for Mac installed in the default app path (`/Applications/Rhino 8.app`)
  - The plugin project references Rhino/Grasshopper assemblies from this location.

### Windows

- Rhino 8 installed in the default path (`C:\Program Files\Rhino 8`)
  - The plugin project references Rhino/Grasshopper assemblies from this location.

### Linux

- MCP host can run for handshake/testing.
- Rhino plugin/live bridge workflow is not supported on Linux.

## Quick Start (Host-Only, All Platforms)

1. Publish the MCP host with a cross-platform `dotnet` command:

```bash
dotnet publish src/LiveCanvas.AgentHost/LiveCanvas.AgentHost.csproj -c Release -o ./dist/agenthost
```

Optional helpers:

```bash
./scripts/publish_agenthost.sh
```

```powershell
pwsh ./scripts/publish_agenthost.ps1
```

2. Verify stdio MCP handshake and tool listing:

```bash
python3 ./scripts/smoke_mcp_stdio.py --agent-host dist/agenthost
```

3. Configure your MCP client to launch the published host (example below).

At this point, MCP transport is verified. This host-only path works on macOS, Windows, and Linux.

If you want the full live Rhino + Grasshopper workflow, continue with the platform-specific steps below. Linux stops at the host-only path.

## Full Solution Build (macOS/Windows + Rhino Only)

If Rhino 8 is installed at the default platform path and you want to build the plugin as well:

```bash
dotnet build LiveCanvas.sln
```

## MCP Client Configuration Example

An example config is provided at:

- `scripts/examples/livecanvas.mcp.config.example.json`

The common shape is:

```json
{
  "mcpServers": {
    "livecanvas": {
      "command": "dotnet",
      "args": ["<repo-root>/dist/agenthost/LiveCanvas.AgentHost.dll"],
      "env": {
        "LIVECANVAS_BRIDGE_URI": "ws://127.0.0.1:17881/livecanvas/v0"
      }
    }
  }
}
```

Notes:

- The `dotnet` + `.dll` form is the safest cross-platform default for config examples.
- On macOS/Linux you can point `command` directly at `dist/agenthost/LiveCanvas.AgentHost` if you prefer.
- On Windows you can point `command` directly at `dist\\agenthost\\LiveCanvas.AgentHost.exe` if you prefer.
- `LIVECANVAS_BRIDGE_URI` is optional. If omitted, the default bridge URI is used.

## Rhino Plugin Setup (Live Bridge)

This section applies to macOS and Windows only.

1. Build plugin:

```bash
dotnet build src/LiveCanvas.RhinoPlugin/LiveCanvas.RhinoPlugin.csproj
```

2. Load the built `.rhp` into Rhino 8.
3. Open Grasshopper in Rhino.
4. Verify bridge reachability through MCP:

```bash
python3 ./scripts/check_live_bridge.py --agent-host dist/agenthost
```

If successful, this returns structured session info from Rhino/Grasshopper.

If Rhino/plugin is not running, the script reports bridge unavailability with a clear message.

## Validation Commands

- Publish host with `dotnet`:
  - `dotnet publish src/LiveCanvas.AgentHost/LiveCanvas.AgentHost.csproj -c Release -o ./dist/agenthost`
- Publish host with shell helper (macOS/Linux/Git Bash):
  - `./scripts/publish_agenthost.sh`
- Publish host with PowerShell helper (Windows):
  - `pwsh ./scripts/publish_agenthost.ps1`
- Build full solution (macOS/Windows + Rhino only):
  - `dotnet build LiveCanvas.sln`
- MCP handshake smoke:
  - `python3 ./scripts/smoke_mcp_stdio.py --agent-host dist/agenthost`
- Live bridge preflight:
  - `python3 ./scripts/check_live_bridge.py --agent-host dist/agenthost`

## Troubleshooting

- `Bridge unavailable`:
  - Ensure Rhino 8 is running and plugin is loaded.
  - Confirm bridge URI matches plugin listener (`LIVECANVAS_BRIDGE_URI`).
- Plugin build fails with missing Rhino assemblies:
  - Verify Rhino 8 is installed at the default path for your OS, or update `.csproj` hint paths accordingly.
- MCP client cannot start host:
  - Check executable permissions (`chmod +x dist/agenthost/LiveCanvas.AgentHost` on macOS/Linux).
  - Try running the host manually from terminal first.
  - On Windows, prefer the documented `dotnet` + `.dll` config if you are unsure which executable path your MCP client expects.

## Current Limitations

- No one-click installer/package yet for plugin distribution.
- Plugin assembly references currently assume default Rhino installation paths.
- Legacy `codelistener.rhi` artifact exists for historical context and is not the active LiveCanvas plugin.
- Some test projects currently rely on local build artifacts and need additional source cleanup for fully deterministic fresh-clone test parity.

## Related Notes

- Historical design notes are in `doc/2026-03-13-rhino8-mac-codelistener-design.md`.
- That document includes background references to legacy MCP paths and is not the canonical runtime setup guide.
