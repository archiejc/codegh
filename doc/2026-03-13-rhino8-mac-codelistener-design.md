# Rhino 8 for Mac CodeListener Replacement Design

Date: 2026-03-13
Status: Approved for implementation planning
Scope: Rhino 8 for Mac only

## 1. Summary

This document defines a replacement for the Windows-only `CodeListener` plugin so that local tools, VS Code, and the existing `GH_mcp_server` can drive Rhino 8 for Mac and Grasshopper in a live coding workflow.

The replacement will not attempt to install or adapt the existing `codelistener.rhi` package. Local inspection of `/Users/jiachenbu/Research/codegh/codelistener.rhi` shows that the packaged `CodeListener.rhp` is a Windows PE32 .NET assembly targeting `.NETFramework,Version=v4.5` and referencing `PresentationFramework` and `System.Windows`, which makes direct use on Rhino 8 for Mac impractical.

Instead, the project will build a new RhinoCommon plugin for Rhino 8 for Mac that:

- Listens on `127.0.0.1:614`
- Accepts the legacy CodeListener JSON request shape
- Executes Rhino Python code in the active Rhino instance
- Supports Grasshopper document manipulation for live coding design workflows
- Preserves a migration path to a richer protocol without breaking the current MCP flow

## 2. Problem Statement

The current repository contains a Python MCP server under `/Users/jiachenbu/Research/codegh/mcps/GH_mcp_server`. That MCP currently assumes a local Rhino-side listener exists. In `/Users/jiachenbu/Research/codegh/mcps/GH_mcp_server/grasshopper_mcp/rhino/connection.py`, the current flow is:

1. Write Python code to a temporary `.py` file
2. Open a TCP connection to `127.0.0.1:614`
3. Send JSON with the fields `filename`, `run`, `reset`, and `temp`
4. Expect the Rhino-side listener to execute the file and return a response

This architecture is viable on Mac if a compatible local listener exists. The missing piece is a Rhino 8 for Mac plugin that provides the listener and execution bridge.

## 3. Goals

- Provide a Rhino 8 for Mac plugin that replaces the Windows-only CodeListener behavior
- Preserve compatibility with the existing local TCP request protocol for the first release
- Support both direct Rhino Python execution and Grasshopper automation
- Support live, incremental updates so repeated MCP requests update existing GH objects instead of duplicating them indefinitely
- Keep the execution bridge local-only and simple enough to debug in a desktop workflow

## 4. Non-Goals

- Rhino 7 compatibility
- Attempting to make the existing Windows `.rhi` package work on Mac
- Full support for arbitrary third-party Grasshopper plugins in the first release
- UI automation that simulates canvas clicks
- Multi-document Grasshopper routing in the first release
- Network-exposed remote execution beyond localhost

## 5. Target Users and Workflow

Primary workflow:

1. User opens Rhino 8 for Mac
2. User loads the new plugin and starts the listener
3. MCP or VS Code sends Python code or Grasshopper graph operations to the plugin
4. The plugin executes the request inside the active Rhino instance
5. The plugin returns structured execution results to the caller
6. The caller decides whether to issue another incremental edit

This supports the intended "live coding design" loop where Codex generates code or graph patches and Rhino reflects the result immediately.

## 6. Platform Constraints and Assumptions

The design is constrained by current Rhino 8 platform behavior and documentation:

- Rhino 8 runs .NET Core on both Windows and Mac, with Mac using .NET Core only
- For Mac-specific Rhino 8 plugins, targeting `.NET 7.0` is a valid baseline, while Rhino 8 later builds may run on .NET 8
- Rhino 8 for Mac plugin development is based on RhinoCommon with Visual Studio Code and Rhino templates
- Rhino 8 scripting infrastructure includes `rhinocode` and the `StartScriptServer` command
- Rhino 8 Grasshopper uses the new unified Script component and supports Python 3 script components
- On Mac, `.macrhi` installers still work, but the official guidance now recommends Package Manager / Yak for distribution

These constraints were validated against official Rhino developer documentation on 2026-03-13.

## 7. Evaluated Approaches

### Approach A: New Mac plugin that emulates the legacy CodeListener protocol

Advantages:

- Minimal MCP changes for phase one
- Fastest path to a working Rhino 8 for Mac loop
- Keeps existing `filename/run/reset/temp` contract intact

Disadvantages:

- Requires reimplementing behavior without the original source
- Must define a clean internal architecture to avoid building another legacy bridge

### Approach B: New plugin and a new protocol at the same time

Advantages:

- Cleaner transport design
- Better room for structured logging and future auth/versioning

Disadvantages:

- Slower time to first working loop
- Requires changing the plugin and MCP together before any end-to-end validation

### Approach C: No listener, just file drop plus manual Rhino command

Advantages:

- Simplest to prototype

Disadvantages:

- Breaks the live coding experience
- Poor fit for incremental MCP-driven design

### Selected Approach

Use Approach A first, but design the implementation so the legacy transport is just one adapter. This keeps the first milestone small without locking the project into the old protocol forever.

## 8. High-Level Architecture

The plugin will be organized into the following runtime modules:

### 8.1 Plugin Commands

Expose at least these Rhino commands:

- `CodeListener`
- `CodeListenerStart`
- `CodeListenerStop`
- `CodeListenerStatus`

`CodeListener` can act as a convenience entry point, while the explicit start/stop/status commands improve diagnosability on Mac.

### 8.2 TCP Listener

- Binds only to `127.0.0.1`
- Defaults to port `614`
- Accepts JSON requests over TCP
- Responds with structured JSON

### 8.3 Protocol Adapter

- Parses the legacy request shape
- Maps old requests onto an internal request model
- Supports future protocol versions without forcing an immediate MCP rewrite

### 8.4 Request Queue and Dispatcher

- Validates requests
- Serializes execution through a single queue
- Ensures all Rhino and Grasshopper mutations happen on the Rhino main thread

### 8.5 Execution Backends

- `RhinoExecutionBackend`
- `GrasshopperPythonBackend`
- `GrasshopperGraphBackend`

### 8.6 State Store

- Maintains request metadata
- Tracks `client_id -> InstanceGuid` mappings for GH objects
- Tracks plugin-owned temporary state for safe reset semantics

### 8.7 Logging

- Writes concise status to the Rhino command line
- Writes structured logs to disk for diagnosis

## 9. Transport and Protocol Design

### 9.1 Legacy Compatibility

The first release must accept this legacy shape:

```json
{
  "filename": "/tmp/script.py",
  "run": true,
  "reset": false,
  "temp": true
}
```

Semantics:

- `filename`: path to a Python file the plugin should read
- `run`: execute request when true
- `reset`: clear plugin-owned state according to target-specific rules
- `temp`: indicates caller may remove the file after execution

### 9.2 Internal Request Model

Internally, requests should be normalized to a richer model:

```json
{
  "request_id": "uuid",
  "protocol_version": 1,
  "target": "rhino|gh-python|gh-graph",
  "filename": "/tmp/script.py",
  "code": null,
  "client_id": null,
  "gh_document": "active",
  "timeout_ms": 15000,
  "reset": false,
  "temp": true
}
```

Rules:

- If only the legacy fields are present, treat the request as `target = "rhino"`
- If future clients send `target`, route accordingly
- If future clients send inline `code`, the plugin may avoid temporary file reads

### 9.3 Response Model

The listener should return structured JSON rather than plain strings:

```json
{
  "ok": true,
  "request_id": "uuid",
  "target": "rhino",
  "stdout": "",
  "stderr": "",
  "error": null,
  "artifacts": {
    "object_ids": [],
    "component_ids": [],
    "wire_count": 0
  },
  "timing_ms": 120
}
```

This response shape is required for reliable tool orchestration and later MCP upgrades.

## 10. Execution Model

### 10.1 Queueing and Threading

Execution must be serialized.

- The TCP listener may accept multiple requests
- Only one request may execute at a time
- Rhino and GH document mutations must be marshaled onto the Rhino main thread
- Background socket threads must never mutate Rhino state directly

This constraint is mandatory for predictable Rhino/GH behavior.

### 10.2 Request Validation

Each request should be validated before execution:

- JSON parses successfully
- Request comes from localhost
- Required fields are present
- If `filename` is supplied, the file exists and is readable
- If `target` is `gh-python` or `gh-graph`, a valid active GH document is available

### 10.3 Reset Semantics

`reset = true` must be conservative:

- `rhino`: clear plugin-owned temporary execution state only
- `gh-python` and `gh-graph`: remove plugin-created, tracked GH objects only

The plugin must never interpret `reset` as "clear the user's Rhino or Grasshopper document."

## 11. Rhino Script Execution

### 11.1 Target: `rhino`

This mode executes Python code against the active Rhino document.

Expected capabilities:

- Create or modify Rhino geometry
- Access RhinoCommon and `rhinoscriptsyntax`
- Return `stdout`, `stderr`, and execution failures

### 11.2 Backend Strategy

Implementation should hide script execution behind an internal abstraction:

- `IRhinoScriptExecutor`

At least two candidate strategies should be supported conceptually:

- `InProcessScriptExecutor`
- `RhinoCodeScriptExecutor`

Recommended order:

1. Prefer in-process execution if RhinoCommon and Rhino 8 APIs provide a stable route
2. Fall back to Rhino 8 scripting infrastructure if direct embedding proves fragile

The fallback is justified because Rhino 8 exposes a script server and `rhinocode` CLI for executing supported scripts in a running Rhino instance.

### 11.3 Compatibility Note

The existing MCP writes Python files and expects Rhino to run them. This means milestone one can succeed even if the first implementation executes files through Rhino 8 script infrastructure rather than through a lower-level embedding API.

## 12. Grasshopper Automation Design

### 12.1 Scope

The first release only manipulates the currently active Grasshopper document.

Not supported in the first release:

- Choosing among multiple open GH documents
- Editing background or headless GH documents
- Multi-user or remote GH sessions

### 12.2 Stable Object Identity

Grasshopper object references should use two layers:

- Internal true ID: `InstanceGuid`
- External stable handle: `client_id`

The plugin maintains a per-document mapping:

- `client_id -> InstanceGuid`

Behavior:

- A new `client_id` creates a new object
- A repeated `client_id` updates the existing object if possible
- If the mapped object no longer exists, the mapping is invalidated and recreated

This is necessary to keep iterative Codex edits from flooding the canvas with duplicates.

## 13. Grasshopper Python Component Mode

### 13.1 Target: `gh-python`

This mode is intended to support the fastest path to an MCP-driven GH workflow.

The first release should support:

- Create a Rhino 8 Grasshopper Script component configured for Python 3
- Update the script source of an existing component
- Update input and output definitions where feasible
- Trigger a new GH solution
- Return component identifiers and execution feedback

### 13.2 Design Choice

The implementation should target Rhino 8's unified Script component with Python 3 semantics, not the legacy GhPython component model.

This is the correct baseline for Rhino 8 for Mac and avoids anchoring the new system to Rhino 7-era assumptions.

### 13.3 Expected Request Shape

Future enriched requests may look like:

```json
{
  "request_id": "uuid",
  "target": "gh-python",
  "client_id": "tower_script",
  "code": "#! python 3\nimport Rhino\n...",
  "inputs": [
    {"name": "x", "access": "item"},
    {"name": "y", "access": "item"}
  ],
  "outputs": [
    {"name": "a"}
  ]
}
```

The exact GH API details can be refined during implementation, but this is the stable contract we should design toward.

## 14. Grasshopper Graph Patch Mode

### 14.1 Target: `gh-graph`

This mode creates and edits native Grasshopper objects directly.

The first release should support only a constrained set of operations:

- Create a component
- Create a parameter object
- Set component position
- Set a basic parameter value
- Connect one component output to one component input
- Replace or remove a tracked connection
- Trigger recomputation
- Summarize the current plugin-owned graph state

### 14.2 Component Addressing

Use a two-level addressing strategy:

- Preferred: explicit `component_guid`
- Secondary: `category`, `subcategory`, and `name`

Reason:

- Name lookup is convenient but more fragile across localization and version changes
- GUID-based lookup is more stable when known

### 14.3 Port Addressing

Use a two-level port strategy:

- Preferred: `source_index` and `target_index`
- Secondary: `source_param` and `target_param`

Reason:

- Indexes are more stable for programmatic routing
- Names are more readable when generating requests from prompts

### 14.4 Explicit First-Release Limits

The first release will not attempt:

- Third-party plugin discovery and support at scale
- Complex Cluster authoring
- Canvas UI automation
- Full graph synthesis from unrestricted natural language in one step

The intended workflow is incremental graph patching.

## 15. Security and Trust Model

The plugin is designed for local desktop automation, not multi-user remote service use.

Security posture for release one:

- Bind only to `127.0.0.1`
- Reject non-local clients
- No external network exposure
- No destructive reset of user-owned model content

Authentication is not required in release one because the system is scoped to a localhost workflow. If the project later evolves toward LAN or remote execution, authentication and explicit user consent gates must be added.

## 16. Logging and Observability

The plugin must log in two places:

### 16.1 Rhino Command Line

Show concise operational messages:

- Listener started
- Listener stopped
- Port in use
- Request accepted
- Request succeeded or failed

### 16.2 File Log

Store structured logs under a plugin-specific directory in the user's Rhino application support tree. A concrete path can be finalized during implementation, but it should live under Rhino 8 for Mac application support and be easy to find manually.

Each log entry should include:

- Timestamp
- `request_id`
- Target
- File path or request summary
- Duration
- Success or failure
- Exception details when relevant

## 17. Error Model

The plugin must report failures as structured categories:

- `protocol_error`
- `validation_error`
- `dispatch_error`
- `execution_error`
- `state_error`

Examples:

- malformed JSON
- unreadable file
- Grasshopper not open for a GH-targeted request
- missing `client_id` mapping
- invalid port name or index
- Python execution exception

This categorization is required so the MCP can eventually make better retry and recovery decisions.

## 18. Installation and Packaging

For local development:

- Build the plugin as a Rhino 8 for Mac RhinoCommon plugin
- Install directly into Rhino 8 for Mac plugin locations during development

For user-facing distribution:

- Prefer Yak / Package Manager packaging
- Optionally support `.macrhi` packaging for manual installation

Rationale:

- Rhino documentation explicitly says `.macrhi` still works, but is no longer in active development
- Package Manager is the preferred long-term distribution channel

## 19. Development Environment Baseline

Recommended baseline:

- Rhino 8 for Mac
- Visual Studio Code
- Rhino templates via `dotnet new install Rhino.Templates`
- New plugin scaffolding via `dotnet new rhino --version 8`
- Target `net7.0` initially for Mac-only compatibility
- Compile managed code as `AnyCPU`

The exact target framework may be revisited later if the active Rhino 8 environment standardizes on a newer runtime, but `net7.0` is the safest initial baseline for this project scope.

## 20. MCP Impact

### 20.1 What can remain unchanged initially

- Local TCP host and port
- Temporary file workflow
- Legacy request fields

This is why milestone one can be achieved with minimal MCP edits.

### 20.2 What must change later

- GH code generation templates currently assume legacy GhPython-style imports and runtime behavior
- GH component creation code in the current MCP is written around Windows and RhinoInside assumptions
- The MCP should be upgraded to use structured response JSON instead of free-form success strings

### 20.3 Planned MCP Extensions

Add explicit targets:

- `target = "rhino"`
- `target = "gh-python"`
- `target = "gh-graph"`

Then add request schemas for:

- script execution
- script component updates
- graph patch operations

## 21. Verification Strategy

The project should use layered verification rather than trying to jump directly to full E2E automation.

### 21.1 Unit Tests

- Protocol parsing
- Request normalization
- Validation logic
- `client_id` mapping behavior
- Response serialization

### 21.2 Integration Tests

- TCP listener receives and parses requests
- Legacy request maps to `target = rhino`
- Request queue executes serially

### 21.3 Manual Acceptance Tests

Milestone one:

- Execute a Python file that creates a point, circle, or curve in Rhino
- Verify `stdout`, `stderr`, and errors return to the caller

Milestone two:

- Create a Python 3 Script component on the active GH canvas
- Update the same component by `client_id`
- Trigger GH recomputation

Milestone three:

- Create a minimal native graph such as `Number Slider -> Construct Point -> Point`
- Re-run the same request and verify update behavior rather than duplicate creation
- Connect and reconnect tracked ports

### 21.4 Failure Case Tests

- Missing file
- Syntax error in Python script
- GH-targeted request with no active GH document
- Invalid `client_id`
- Invalid port reference

## 22. Milestones

### M0: Plugin Skeleton

- Rhino 8 for Mac plugin builds and loads
- Commands exist: start, stop, status
- TCP listener starts and returns a basic JSON heartbeat

### M1: Rhino Script Execution

- Legacy protocol accepted
- Python file execution works in active Rhino
- Structured response returned
- Existing MCP can trigger Rhino-side execution

### M2: Grasshopper Python Workflow

- Create or update a Python 3 Script component
- Use `client_id` mapping
- Trigger GH solution
- Return component identifiers and logs

### M3: Grasshopper Graph Patch Workflow

- Create tracked native components and parameters
- Connect and update tracked wires
- Return component and wire summaries

### M4: MCP Modernization

- Add explicit targets and structured request builders
- Move GH code templates to Rhino 8 / Python 3 Script semantics
- Use structured responses to drive iterative design workflows

## 23. Risks and Mitigations

### Risk 1: Rhino 8 Mac scripting API details differ from the assumed execution path

Mitigation:

- Hide execution behind backend interfaces
- Allow fallback to Rhino 8 script infrastructure

### Risk 2: Grasshopper object APIs for the unified Script component are less direct than expected

Mitigation:

- Make `gh-python` a dedicated milestone
- Treat graph patching as a separate backend

### Risk 3: Legacy MCP assumptions leak Windows-era behavior into the Mac implementation

Mitigation:

- Keep the external protocol but redesign the internal architecture
- Update GH templates in a later MCP milestone

### Risk 4: Live coding floods the GH canvas with duplicate objects

Mitigation:

- Require `client_id` for persistent GH operations
- Track object ownership and mappings per document

## 24. Recommended Implementation Order

1. Create the Rhino 8 for Mac plugin skeleton
2. Implement localhost TCP listener and legacy protocol adapter
3. Implement Rhino Python file execution
4. Validate end-to-end with the existing MCP
5. Implement `gh-python` component creation and update
6. Implement `gh-graph` patch operations
7. Upgrade the MCP to use richer request and response models

## 25. Open Technical Questions for Implementation

These questions do not block planning but must be answered during implementation:

- Which Rhino 8 API path is the most reliable for in-process Python execution on Mac?
- What is the most robust API path for programmatically creating and editing Rhino 8 Grasshopper Script components?
- Which exact application support path should be used for plugin-owned logs on Rhino 8 for Mac?
- Should the plugin persist `client_id` mappings only in memory, or also embed recoverable metadata into GH object nicknames/user strings?

## 26. Decision Record

Approved decisions:

- Target Rhino 8 for Mac only
- Support both Rhino script execution and Grasshopper workflows
- Preserve legacy CodeListener TCP compatibility first
- Add richer internal protocol without breaking phase-one MCP usage
- Prioritize `rhino` execution first, then `gh-python`, then `gh-graph`
- Store the design spec in the repository under `doc/`

## 27. References

Official Rhino documentation reviewed on 2026-03-13:

- Rhino 8 .NET runtime and migration: https://developer.rhino3d.com/en/guides/rhinocommon/moving-to-dotnet-core/
- RhinoCommon changes in Rhino 8: https://developer.rhino3d.com/guides/rhinocommon/whats-new/
- Rhino for Mac plugin setup: https://developer.rhino3d.com/en/guides/rhinocommon/installing-tools-mac/
- RhinoCommon plugin template on Mac: https://developer.rhino3d.com/en/guides/rhinocommon/your-first-plugin-mac/
- Mac plugin installation and `.macrhi`: https://developer.rhino3d.com/guides/rhinocommon/plugin-installers-mac/
- Yak / Package Manager packaging: https://developer.rhino3d.com/en/guides/yak/creating-a-multi-targeted-rhino-plugin-package/
- RhinoCode CLI and `StartScriptServer`: https://developer.rhino3d.com/guides/scripting/advanced-cli
- Rhino 8 ScriptEditor command usage: https://developer.rhino3d.com/guides/scripting/scripting-command/
- Rhino 8 Grasshopper Script component: https://developer.rhino3d.com/en/guides/scripting/scripting-component/
- Rhino 8 Grasshopper Python scripting reference: https://developer.rhino3d.com/guides/scripting/scripting-gh-python/
