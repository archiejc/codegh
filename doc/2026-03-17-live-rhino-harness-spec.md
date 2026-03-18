# Live Rhino Harness Upgrade Spec

Date: 2026-03-17
Status: Approved for implementation
Scope: Dual-mode smoke harness for mock and live Rhino 8 validation

## 1. Summary

This document defines how to upgrade the existing `tools/LiveCanvas.SmokeHarness` from a mock-only smoke tool into a dual-mode harness.

The upgraded harness must support:

- `mock` mode as the default CI-safe regression baseline
- `live` mode that attaches to an already running Rhino 8 session with `LiveCanvas.RhinoPlugin` loaded and validates the real chain:
  - external coding agent
  - MCP stdio
  - `LiveCanvas.AgentHost`
  - Rhino plugin localhost WebSocket bridge
  - real Grasshopper document
  - solve
  - Rhino viewport preview capture
  - `.gh` save

The harness upgrade must not change the public MCP tool surface, bridge JSON-RPC method names, or request/response contracts.

## 2. Non-Goals

- Changing MCP tool names, request shapes, or response shapes
- Changing bridge JSON-RPC method names or envelope structure
- Automatically launching Rhino on macOS or Windows
- Automatically installing or loading the Rhino plugin
- Expanding the smoke graph beyond one fixed deterministic baseline graph
- Running live Rhino validation by default in CI

## 3. Existing Constraints

The implementation must fit the current architecture:

- `LiveCanvas.AgentHost` is an external stdio MCP process
- `LiveCanvas.RhinoPlugin` starts the localhost bridge in `OnLoad()`
- the default bridge URI is `ws://127.0.0.1:17881/livecanvas/v0`
- the bridge URI can already be overridden through `LIVECANVAS_BRIDGE_URI`
- the current smoke harness lives under `tools/LiveCanvas.SmokeHarness`

This work must stay within the harness and its E2E coverage unless a blocking issue is found.

## 4. Goal And Success Criteria

### 4.1 Goal

Turn the current smoke harness into the canonical verification tool for both:

- local mock-based regression checks without Rhino
- first real integration checks against Rhino 8 and Grasshopper

### 4.2 Live Success Criteria

A `live` run is successful only if all of the following are true:

1. The harness connects directly to the live Rhino bridge WebSocket.
2. Direct bridge `gh_session_info` returns:
   - `rhinoRunning = true`
   - `grasshopperLoaded = true`
3. `LiveCanvas.AgentHost` starts and completes:
   - `initialize`
   - `notifications/initialized`
   - `tools/list`
4. `tools/list` exactly matches the current v0 tool surface.
5. The first Grasshopper document creation happens through the MCP and `AgentHost` path, not in preflight.
6. The harness builds the fixed smoke graph on a real Grasshopper document.
7. `gh_solve` succeeds without errors.
8. `gh_inspect_document` reports:
   - the expected component count
   - the expected connection count
   - `previewSummary.hasGeometry = true`
   - `previewSummary.previewObjectCount > 0`
   - `boundingBox != null`
9. `gh_capture_preview` writes a real image file.
10. `gh_save_document` writes a real `.gh` file.
11. During manual live smoke, the operator verifies that the Grasshopper canvas updates in-session and shows the expected nodes and wires while the harness runs.
12. The harness exits with code `0`.

### 4.3 Cross-Platform Acceptance

Live-mode validation is not complete until the harness is manually or opt-in automatically validated on both:

- macOS Rhino 8
- Windows Rhino 8

Passing on one OS is not sufficient for final acceptance.

## 5. Dual-Mode Design

### 5.1 Modes

The harness supports exactly two modes:

- `mock`
- `live`

`mock` remains the default mode.

### 5.2 Shared Logic Rule

The following logic must be shared between `mock` and `live`:

- MCP initialize flow
- MCP `tools/list` validation
- component add/configure/connect sequence
- solve
- inspect
- capture
- save
- stdout summary formatting
- manifest and transcript structure

Only the following may differ by mode:

- bridge acquisition
- bridge URI source
- live preflight behavior
- assertion strictness where mock mode cannot prove real Rhino behavior

### 5.3 Required Internal Types

Add internal harness-only types for:

- `SmokeHarnessMode`
- `HarnessRunContext`
- `SmokeArtifactManifest`
- `SmokeTranscriptEvent`

No public product API changes are allowed.

## 6. Smoke Graph Definition

The harness must use one fixed deterministic smoke graph in both modes.

### 6.1 Components

Add these components in this exact order:

1. `number_slider` as width
2. `number_slider` as depth
3. `number_slider` as height
4. `xy_plane`
5. `rectangle`
6. `boundary_surfaces`
7. `vector_xyz`
8. `extrude`

### 6.2 Canvas Positions

Use these exact positions:

- width slider: `(40, 20)`
- depth slider: `(40, 70)`
- height slider: `(500, 20)`
- xy plane: `(40, 120)`
- rectangle: `(260, 120)`
- boundary surfaces: `(500, 120)`
- vector xyz: `(720, 120)`
- extrude: `(940, 120)`

### 6.3 Configurations

Apply only these value configs:

- width slider:
  - nickname `Width`
  - `min = 1`
  - `max = 50`
  - `value = 20`
  - `integer = false`
- depth slider:
  - nickname `Depth`
  - `min = 1`
  - `max = 50`
  - `value = 12`
  - `integer = false`
- height slider:
  - nickname `Height`
  - `min = 1`
  - `max = 80`
  - `value = 18`
  - `integer = false`

Optional readability nicknames are allowed for:

- `XY`
- `Rect`
- `Brep`
- `Vec`
- `Extrude`

### 6.4 Connections

Use these exact canonical port names:

1. `xy_plane.P -> rectangle.P`
2. `width_slider.N -> rectangle.X`
3. `depth_slider.N -> rectangle.Y`
4. `rectangle.R -> boundary_surfaces.E`
5. `boundary_surfaces.S -> extrude.B`
6. `height_slider.N -> vector_xyz.Z`
7. `vector_xyz.V -> extrude.D`

## 7. CLI Contract

The harness remains a single executable project under `tools/LiveCanvas.SmokeHarness`.

### 7.1 Default Invocation

```bash
dotnet run --project tools/LiveCanvas.SmokeHarness/LiveCanvas.SmokeHarness.csproj
```

Defaults:

- mode = `mock`
- run direct bridge check and MCP check
- configuration = `Debug`

### 7.2 Supported Options

Support exactly these flags:

- `--mode <mock|live>`
- `--agent-host-project <path>`
- `--agent-host-dll <path>`
- `--skip-build-agent-host`
- `--output-dir <path>`
- `--configuration <Debug|Release>`
- `--bridge-only`
- `--mcp-only`
- `--bridge-uri <ws://...>` only in `live`
- `--live-preflight-timeout-seconds <int>` only in `live`, default `10`

### 7.3 Invalid Combinations

Fail fast with exit code `2` and one `[error] ...` line for:

- `--mode mock --bridge-uri ...`
- `--mode mock --live-preflight-timeout-seconds ...`
- `--bridge-only --mcp-only`
- unknown flags
- missing values
- malformed live bridge URI

## 8. Live Mode Operating Model

### 8.1 Live Mode Is Attach-Only

`live` mode attaches to an already running Rhino 8 instance. It does not:

- launch Rhino
- install the plugin
- load the plugin automatically

### 8.2 Live Preconditions

Before running `--mode live`, the operator must:

1. build `LiveCanvas.RhinoPlugin`
2. open Rhino 8
3. load `LiveCanvas.RhinoPlugin.rhp`
4. ensure the plugin bridge is listening
5. keep Rhino open through the run

### 8.3 Live Preflight

Preflight in `live` mode must:

1. connect directly to the bridge URI
2. call `gh_session_info`
3. require:
   - `rhinoRunning = true`
   - `grasshopperLoaded = true`

Preflight must not call `gh_new_document`. The first document creation must happen through the MCP and `AgentHost` path so the live chain remains cleanly validated end-to-end.

## 9. MCP Smoke Sequence

Run this sequence in both modes:

1. start `AgentHost` as an external process using `dotnet <resolved-agent-host-dll>`
2. send `initialize`
3. send `notifications/initialized`
4. send `tools/list`
5. call `gh_session_info`
6. call `gh_new_document`
7. call `gh_list_allowed_components`
8. add all smoke graph components
9. configure sliders and nicknames
10. connect all ports
11. call `gh_inspect_document`
12. call `gh_solve`
13. call `gh_inspect_document` again
14. call `gh_capture_preview`
15. call `gh_save_document`
16. close stdin and wait for `AgentHost` to exit

Assertions:

- `tools/list` must exactly match the current v0 tool ordering
- first inspect:
  - `components.length == 8`
  - `connections.length == 7`
- solve:
  - `solved = true`
  - fail if any explicit error is returned
- second inspect:
  - `previewSummary.hasGeometry = true`
  - `previewSummary.previewObjectCount > 0`
  - `boundingBox != null`
- capture:
  - `captured = true`
  - file exists
  - file length > 0
- save:
  - `saved = true`
  - file exists
  - file length > 0
  - extension is `.gh`

### 9.1 Warning Handling

Review reconciliation:

- `warning` in `live` mode is recorded in the transcript and manifest
- `warning` does not fail the run by itself
- the run fails if warnings coincide with missing geometry, missing artifacts, or explicit error conditions

This keeps the live harness aligned with the actual goal of validating the real chain without becoming brittle across Rhino platform differences.

## 10. Output Contract

Each run must write:

- `preview.png`
- `smoke.gh`
- `manifest.json`
- `transcript.json`

### 10.1 Manifest

`manifest.json` must include at least:

- mode
- bridge URI
- output directory
- preview path
- gh path
- transcript path
- completed checks
- success
- errors
- start time
- finish time
- session summary:
  - platform
  - Rhino version
  - tool version

### 10.2 Transcript

`transcript.json` must record ordered events for:

- direct bridge request/response pairs
- MCP request/response pairs
- process start/stop events
- artifact write events
- failures

Each event must include:

- sequence
- phase
- transport
- method
- request
- response
- success
- timestamp UTC

## 11. Failure Taxonomy

Map failures into these categories:

- `cli_usage`
- `bridge_unreachable`
- `bridge_protocol_error`
- `agenthost_build_failed`
- `agenthost_start_failed`
- `mcp_protocol_error`
- `tool_surface_mismatch`
- `tool_call_failed`
- `artifact_missing`
- `live_precondition_failed`

Use direct, single-line error messages on stderr.

## 12. Test Plan

### 12.1 Automated, No-Rhino Tests

These must remain runnable under normal `dotnet test`:

- existing mock smoke E2E remains green
- CLI parsing tests
- invalid-combination tests
- manifest generation tests in mock mode
- transcript generation tests in mock mode
- mode-specific check-name tests

### 12.2 Optional Live Test

Add one opt-in live test gated by:

`LIVECANVAS_RUN_LIVE_SMOKE=1`

It must:

- skip by default
- require pre-running Rhino with plugin loaded
- run the harness in `live` mode
- validate success and artifact presence

### 12.3 Manual Live Smoke

Manual live smoke is required for real integration acceptance.

Checklist:

1. build `AgentHost`
2. build `LiveCanvas.RhinoPlugin` for the current OS
3. open Rhino 8
4. load `LiveCanvas.RhinoPlugin.rhp`
5. run:

```bash
dotnet run --project tools/LiveCanvas.SmokeHarness/LiveCanvas.SmokeHarness.csproj -- --mode live
```

6. verify:
   - exit code `0`
   - `[ok] bridge-jsonrpc-live`
   - `[ok] mcp-stdio-live`
   - Grasshopper shows the expected 8 components and 7 wires live in-session
   - `preview.png` contains the extruded model
   - `smoke.gh` opens in Grasshopper and contains the expected graph

## 13. Platform Notes

### 13.1 macOS

Build plugin:

```bash
dotnet build src/LiveCanvas.RhinoPlugin/LiveCanvas.RhinoPlugin.csproj -c Debug -f net8.0
```

Expected plugin path:

`src/LiveCanvas.RhinoPlugin/bin/Debug/net8.0/LiveCanvas.RhinoPlugin.rhp`

### 13.2 Windows

Build plugin:

```powershell
dotnet build src/LiveCanvas.RhinoPlugin/LiveCanvas.RhinoPlugin.csproj -c Debug -f net8.0-windows
```

Expected plugin path:

`src/LiveCanvas.RhinoPlugin/bin/Debug/net8.0-windows/LiveCanvas.RhinoPlugin.rhp`

## 14. Implementation Boundaries

Expected modified areas:

- `tools/LiveCanvas.SmokeHarness/Program.cs`
- `tools/LiveCanvas.SmokeHarness/SmokeHarnessRunner.cs`
- harness-only internal files added under `tools/LiveCanvas.SmokeHarness/`
- E2E coverage under `tests/LiveCanvas.E2E.Tests/`

Do not modify as part of this harness upgrade:

- AgentHost public tool contracts
- Rhino plugin runtime behavior
- bridge protocol contracts

## 15. Acceptance Checklist

Work is complete only when all are true:

- mock mode remains green under `dotnet test`
- harness supports `--mode live`
- live mode does not start `MockBridgeServer`
- live mode validates a real Rhino bridge session
- shared smoke graph runs through `AgentHost` over MCP stdio
- preview and `.gh` artifacts are written in live mode
- no public tool-surface changes were introduced
- manual or opt-in live smoke passes on macOS Rhino 8
- manual or opt-in live smoke passes on Windows Rhino 8
- output includes `manifest.json` and `transcript.json`
