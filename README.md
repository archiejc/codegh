# LiveCanvas MCP for Rhino + Grasshopper

This repository contains the LiveCanvas runtime stack for Rhino 8 and Grasshopper:

- a stdio MCP host in `src/LiveCanvas.AgentHost`
- a Rhino plugin in `src/LiveCanvas.RhinoPlugin`
- a local WebSocket bridge between them at `ws://127.0.0.1:17881/livecanvas/v0`

The active MCP entrypoint is `src/LiveCanvas.AgentHost`.

The legacy folder `mcps/GH_mcp_server` is currently empty and is not the runtime entrypoint for this repository.

## What Is In This Repo

Core projects:

- `src/LiveCanvas.AgentHost`
  `stdio` MCP host exposing `gh_*` tools
- `src/LiveCanvas.RhinoPlugin`
  Rhino plugin hosting the local bridge
- `src/LiveCanvas.Bridge.Protocol`
  JSON-RPC method names and serialization helpers
- `src/LiveCanvas.Contracts`
  request and response contracts
- `src/LiveCanvas.Core`
  allowed-component registry and validation logic
- `tools/LiveCanvas.SmokeHarness`
  smoke harness for mock and live runtime checks

Supporting scripts:

- `scripts/publish_agenthost.sh`
  publish the MCP host on macOS/Linux or Git Bash
- `scripts/publish_agenthost.ps1`
  publish the MCP host on Windows PowerShell
- `scripts/smoke_mcp_stdio.py`
  verify MCP `initialize` and `tools/list`
- `scripts/check_live_bridge.py`
  verify that MCP can reach the Rhino bridge through `gh_session_info`
- `scripts/install-mcp-livecanvas-mac.sh`
  macOS-focused setup helper for Codex and Claude Code

## Runtime Architecture

```text
Agent / IDE
  -> MCP stdio
  -> LiveCanvas.AgentHost
  -> ws://127.0.0.1:17881/livecanvas/v0
  -> LiveCanvas.RhinoPlugin (inside Rhino 8)
  -> Grasshopper document mutations, solve, inspect, capture, save
```

## Prerequisites

### Common

- `.NET SDK 8.x`
- Git
- Python 3 for the smoke scripts under `scripts/`

### macOS

- Rhino 8 for Mac installed at `/Applications/Rhino 8.app`
- Grasshopper available in Rhino
- Optional: Terminal Accessibility permission if you want to use the scripted UI helpers

### Windows

- Rhino 8 installed at `C:\Program Files\Rhino 8`
- Grasshopper available in Rhino

### Linux

- Host-only MCP publish and stdio smoke verification are supported
- Rhino plugin and live bridge workflow are not supported

## Quick Start (Host-Only, All Platforms)

This path is the safest fresh-clone entrypoint and does not require Rhino.

1. Publish the MCP host:

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

3. Configure your MCP client to launch the published host.

At this point, MCP transport is verified. This host-only path works on macOS, Windows, and Linux.

## MCP Client Configuration Example

Example file:

- `scripts/examples/livecanvas.mcp.config.example.json`

Recommended cross-platform form:

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

- `LIVECANVAS_BRIDGE_URI` is optional. If omitted, the default bridge URI is used.
- On macOS/Linux you can point `command` directly at `dist/agenthost/LiveCanvas.AgentHost`.
- On Windows you can point `command` directly at `dist\\agenthost\\LiveCanvas.AgentHost.exe`.

## Full Live Workflow

### macOS One-Command Setup

After cloning this repository, you can build, deploy, and register the MCP server for Codex and Claude Code with:

```bash
bash scripts/install-mcp-livecanvas-mac.sh --target both
```

Single-client variants:

```bash
bash scripts/install-mcp-livecanvas-mac.sh --target codex
bash scripts/install-mcp-livecanvas-mac.sh --target claude
```

What this does:

- builds the Rhino plugin, AgentHost, and smoke harness
- deploys the Rhino plugin into Rhino 8 MacPlugIns
- writes a `livecanvas` MCP stdio entry into the selected client config file

After the install script finishes:

1. Restart Codex Desktop and or Claude Code.
2. Open Rhino 8.
3. Create a Rhino document.
4. Open Grasshopper.
5. Ask the client to call `gh_session_info`.

### macOS Manual Live Setup

Build from source:

```bash
dotnet build LiveCanvas.sln -v minimal
```

Optional helper:

```bash
scripts/build-rhino-plugin-mac.sh Debug
```

Deploy plugin to Rhino MacPlugIns:

```bash
scripts/deploy-rhino-plugin-mac.sh Debug
```

This syncs:

- `~/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/LiveCanvas.RhinoPlugin/`
- `~/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/LiveCanvas.RhinoPlugin.rhp/`

Open Rhino and Grasshopper:

```bash
scripts/open-rhino-grasshopper-mac.sh
```

Manual alternative:

1. Open Rhino 8.
2. Create a new document.
3. Run `Grasshopper`.

### Windows Live Setup

There is no Windows helper script yet. The supported manual path is:

1. Build `src/LiveCanvas.RhinoPlugin/LiveCanvas.RhinoPlugin.csproj`
2. Load the built `.rhp` into Rhino 8
3. Open Grasshopper
4. Publish `LiveCanvas.AgentHost`
5. Run the bridge preflight shown below

## Copilot Tools

`LiveCanvas.AgentHost` now exposes two copilot MCP tools in addition to the existing `gh_*` bridge tools:

- `copilot_plan`
  - host-only capable on macOS, Windows, and Linux
  - accepts `prompt` plus optional `image_paths`
  - validates image paths before any model call
  - returns a server-emitted `execution_plan`
- `copilot_apply_plan`
  - requires the live Rhino + Grasshopper bridge
  - executes a previously emitted `execution_plan`
  - writes `preview.png` and `document.gh` into the selected output directory

`copilot_plan` image constraints:

- paths must be absolute local paths
- up to 4 images
- extensions limited to `.png`, `.jpg`, `.jpeg`

The current v1 copilot contract is intentionally strict:

- `execution_plan` is public transport payload, but not an end-user editable surface
- apply only supports `schema_version = "copilot_execution_plan/v1"`
- apply creates a new document each attempt
- no session memory, no existing-definition edit flow, no third-party plugins

## Copilot Provider Configuration

`copilot_plan` uses an OpenAI-compatible `POST /chat/completions` provider configured through environment variables:

- `LIVECANVAS_COPILOT_BASE_URL`
- `LIVECANVAS_COPILOT_API_KEY`
- `LIVECANVAS_COPILOT_MODEL`

If these are missing, `copilot_plan` returns a clear MCP tool-unavailable error instead of failing with an internal server error.

## Copilot Workflow Example

Example `copilot_plan` call:

```json
{
  "name": "copilot_plan",
  "arguments": {
    "prompt": "Create a stepped tower with a low podium and a slender upper volume",
    "image_paths": ["/absolute/path/to/reference.png"]
  }
}
```

Example `copilot_apply_plan` call:

```json
{
  "name": "copilot_apply_plan",
  "arguments": {
    "execution_plan": "<structuredContent from copilot_plan>",
    "output_dir": "/absolute/path/to/output",
    "preview_width": 1600,
    "preview_height": 900,
    "expire_all": true
  }
}
```

Expected fixed artifacts after a successful apply:

- `preview.png`
- `document.gh`

## Copilot Manual Acceptance Checklist

Use this checklist before handing the feature to broader users:

1. Run `copilot_plan` with one text prompt.
2. Run `copilot_plan` with one text prompt plus one local image.
3. Confirm the returned `execution_plan.schema_version` is `copilot_execution_plan/v1`.
4. Run `copilot_apply_plan` against a live Rhino + Grasshopper session.
5. Confirm the Grasshopper canvas mutates visibly.
6. Confirm the output directory contains `preview.png` and `document.gh`.
7. Keep the smoke harness `manifest.json` and `transcript.json` for traceability.

## Validation Commands

Host-only:

- Publish host with `dotnet`
  - `dotnet publish src/LiveCanvas.AgentHost/LiveCanvas.AgentHost.csproj -c Release -o ./dist/agenthost`
- Publish host with shell helper
  - `./scripts/publish_agenthost.sh`
- Publish host with PowerShell helper
  - `pwsh ./scripts/publish_agenthost.ps1`
- MCP handshake smoke
  - `python3 ./scripts/smoke_mcp_stdio.py --agent-host dist/agenthost`

Live bridge:

- Bridge preflight through MCP
  - `python3 ./scripts/check_live_bridge.py --agent-host dist/agenthost`
- Bridge preflight but do not fail the shell when Rhino is offline
  - `python3 ./scripts/check_live_bridge.py --agent-host dist/agenthost --allow-offline`

macOS live smoke harness:

- Bridge only
  - `scripts/live-smoke-mac.sh --bridge-only --timeout-seconds 30`
- Full live chain
  - `scripts/live-smoke-mac.sh --timeout-seconds 30`
- One-shot dev flow
  - `scripts/dev-smoke-mac.sh --timeout-seconds 30`
- Skip open if Rhino and Grasshopper are already running
  - `scripts/dev-smoke-mac.sh --skip-open --timeout-seconds 30`

Direct smoke harness command:

```bash
dotnet run --project tools/LiveCanvas.SmokeHarness/LiveCanvas.SmokeHarness.csproj -- --mode live --live-preflight-timeout-seconds 30
```

Mock copilot smoke harness command:

```bash
dotnet run --project tools/LiveCanvas.SmokeHarness/LiveCanvas.SmokeHarness.csproj -- --scenario copilot-absolute-towers
```

Live copilot smoke harness command:

```bash
dotnet run --project tools/LiveCanvas.SmokeHarness/LiveCanvas.SmokeHarness.csproj -- --mode live --scenario copilot-absolute-towers --live-preflight-timeout-seconds 30
```

## Use From An MCP-Capable Coding Agent

On macOS, the repo already includes a launcher tailored to the Rhino bridge workflow:

```bash
/absolute/path/to/scripts/run-agenthost-mac.sh --skip-build
```

If you want it to rebuild `LiveCanvas.AgentHost` before startup:

```bash
/absolute/path/to/scripts/run-agenthost-mac.sh
```

If your client needs an explicit bridge URI:

```bash
/absolute/path/to/scripts/run-agenthost-mac.sh --skip-build --bridge-uri ws://127.0.0.1:17881/livecanvas/v0
```

For portable client configs across machines, prefer the documented `dotnet` + `.dll` form shown above.

## Modeling Demo

The repo includes an Absolute Towers style live demo scene:

```bash
bash scripts/demo-absolute-towers-mac.sh --timeout-seconds 30
```

Expected stdout includes:

- `[ok] bridge-jsonrpc-live`
- `[ok] mcp-stdio-live`
- `output_dir=...`

Expected artifacts inside the printed output directory:

- `absolute-towers.gh`
- `manifest.json`
- `transcript.json`

If Rhino exposes an active document and viewport, the run may also write `preview.png`.

## Troubleshooting

- `Bridge unavailable`
  - Ensure Rhino 8 is running and the plugin is loaded.
  - Confirm the bridge URI matches the plugin listener.
- Plugin build fails with missing Rhino assemblies
  - Verify Rhino 8 is installed at the default path for your OS, or update the `.csproj` hint paths.
- MCP client cannot start host
  - On macOS/Linux, check executable permissions on `dist/agenthost/LiveCanvas.AgentHost`.
  - On Windows, prefer the `dotnet` + `.dll` form if you are unsure which executable path your MCP client expects.
- Smoke scripts hang or stall
  - Both Python smoke scripts honor `--timeout-seconds` and will now fail fast if the host starts but does not answer.

## Current Limitations

- No one-click cross-platform installer exists for the Rhino plugin.
- Windows helper scripts are still missing; the richer helper flow is macOS-first.
- Plugin assembly references assume default Rhino installation paths.
- The legacy `codelistener.rhi` artifact remains in the repository for historical context and is not the active LiveCanvas plugin.
- Some test projects still need cleanup for fully deterministic fresh-clone test parity.
- Copilot v1 always creates a new document and does not edit an existing Grasshopper definition.
- Copilot v1 only uses dimensions and structural hints to drive output; richer visual style hints are preserved in the brief but not applied to geometry generation.
- Copilot execution plans are emitted by the server and are not a supported hand-authored editing surface.

## Related Notes

- Historical design notes live in `doc/2026-03-13-rhino8-mac-codelistener-design.md`
- That design document still contains historical references to an old Python MCP path and should not be treated as the canonical setup guide
