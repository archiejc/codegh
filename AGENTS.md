# Repository Guidelines

## Project Structure & Module Organization

This LiveCanvas repository is a .NET 8.x MCP host and Rhino plugin for Grasshopper:

```
src/                          # Core source code
├── LiveCanvas.AgentHost/     # MCP host entrypoint (stdio)
├── LiveCanvas.RhinoPlugin/   # Rhino 8 plugin with bridge
├── LiveCanvas.Bridge.Protocol/  # JSON-RPC method names & serialization
├── LiveCanvas.Contracts/     # Request/response contracts
└── LiveCanvas.Core/          # Component registry & validation

tests/                        # Test projects (xUnit)
├── LiveCanvas.AgentHost.Tests/
├── LiveCanvas.RhinoPlugin.Tests/
├── LiveCanvas.Bridge.Protocol.Tests/
├── LiveCanvas.Contracts.Tests/
├── LiveCanvas.Core.Tests/
└── LiveCanvas.E2E.Tests/

scripts/                      # Helper scripts (Python, Bash, PowerShell)
docs/                         # Documentation
mcps/                         # Legacy MCP server directory (currently empty)
```

## Build, Test, and Development Commands

### Build
```bash
# Publish MCP host (host-only, no Rhino required)
dotnet publish src/LiveCanvas.AgentHost/LiveCanvas.AgentHost.csproj \
  -c Release -o ./dist/agenthost

# Helper scripts
./scripts/publish_agenthost.sh           # macOS/Linux
pwsh ./scripts/publish_agenthost.ps1     # Windows PowerShell
```

### Test
```bash
# Run all unit tests
dotnet test

# Run specific test project
dotnet test tests/LiveCanvas.Core.Tests/
```

### Smoke Verification
```bash
# MCP stdio handshake and tool listing
python scripts/smoke_mcp_stdio.py

# Live bridge verification (requires Rhino)
python scripts/check_live_bridge.py
```

## Coding Style & Naming Conventions

- **Target framework**: .NET 8 (some projects also target .NET 7)
- **Language features**: `<LangVersion>latest</LangVersion>`
- **Nullable reference types**: Enabled globally
- **Implicit usings**: Enabled globally
- **Treat warnings as errors**: Disabled

### Naming Patterns
- Projects follow `LiveCanvas.<Module>` naming convention
- Classes use PascalCase (e.g., `LiveCanvasRuntime`, `BridgeJsonSerializer`)
- Test classes suffix with `Tests` (e.g., `ComponentRegistryTests`)

### Test Configuration
All `*.Tests` projects include global `Xunit` using via `Directory.Build.props`.

## Testing Guidelines

- **Framework**: xUnit (globally configured in `Directory.Build.props`)
- **Naming**: Test classes end with `Tests`, test methods use descriptive names
- **Run tests**: `dotnet test` runs all test projects
- **Test scope**: Unit tests for core logic, E2E tests for integration flows

## Commit & Pull Request Guidelines

### Commit Messages
Follow conventional commit style observed in project history:
- Simple summaries: `Improve MCP onboarding and smoke tooling`
- Scoped changes: `docs: add copilot mvp implementation plan`
- Merge commits from PRs: `Merge pull request #2 from <branch>`

### Pull Request Requirements
- Link related issues when applicable
- Provide clear description of changes
- For UI or bridge changes, include screenshots or smoke test results
- Ensure `dotnet test` passes before merging

## Architecture Overview

```
Agent/IDE → MCP stdio → LiveCanvas.AgentHost
          → ws://127.0.0.1:17881/livecanvas/v0
          → LiveCanvas.RhinoPlugin (inside Rhino 8)
          → Grasshopper: component mutation, solve, inspect, capture, save
```

The active runtime entrypoint is `src/LiveCanvas.AgentHost`, not the legacy `mcps/GH_mcp_server` directory.

## Platform Notes

- **macOS**: Rhino 8 at `/Applications/Rhino 8.app`; Terminal accessibility required for scripted UI helpers
- **Windows**: Rhino 8 at `C:\Program Files\Rhino 8`
- **Linux**: Host-only MCP publish and stdio smoke verification supported; Rhino plugin workflow not supported
