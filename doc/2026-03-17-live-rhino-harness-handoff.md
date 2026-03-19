# Live Rhino Harness Handoff

Date: 2026-03-17
Branch: `codex/live-canvas-builder-v0`
Remote: `origin https://github.com/archiejc/codegh.git`
Latest pushed base commit before this checkpoint: `6b4b191`

## 1. Summary

This checkpoint preserves the current smoke harness WIP and the implementation spec for upgrading it into a live Rhino harness.

The intent is to let another Codex session on another machine resume work without reconstructing context from chat history.

## 2. Current Local WIP Included In This Checkpoint

Harness code currently in progress:

- `tools/LiveCanvas.SmokeHarness/LiveCanvas.SmokeHarness.csproj`
- `tools/LiveCanvas.SmokeHarness/Program.cs`
- `tools/LiveCanvas.SmokeHarness/SmokeHarnessRunner.cs`
- `tools/LiveCanvas.SmokeHarness/MockBridgeServer.cs`
- `tests/LiveCanvas.E2E.Tests/LiveCanvas.E2E.Tests.csproj`
- `tests/LiveCanvas.E2E.Tests/SmokeHarnessRunnerTests.cs`

## 3. What Is Already True

The current checkpoint already proves the local baseline for the architecture:

- mock bridge smoke works
- MCP stdio smoke works
- E2E smoke test passes
- the full solution test run passes with the new mock-based E2E included

Verification commands used for this checkpoint:

```bash
dotnet run --project tools/LiveCanvas.SmokeHarness -- --bridge-only
dotnet run --project tools/LiveCanvas.SmokeHarness -- --mcp-only
dotnet run --project tools/LiveCanvas.SmokeHarness
dotnet test tests/LiveCanvas.E2E.Tests/LiveCanvas.E2E.Tests.csproj --filter SmokeHarnessRunnerTests -v minimal
dotnet test LiveCanvas.sln -v minimal
```

Expected successful signals:

- `[ok] bridge-jsonrpc`
- `[ok] mcp-stdio`
- E2E smoke test passes
- solution tests pass

## 4. What Is Not Done Yet

The real target is not complete yet.

Still missing:

- `--mode live` is not implemented in the harness
- the harness does not yet attach to a real Rhino bridge
- no real Rhino 8 end-to-end validation has been completed
- no live artifact manifest or transcript has been added yet
- no cross-platform live validation on macOS and Windows has been completed yet

## 5. Current Meaning Of The Harness

The current harness proves:

- MCP stdio framing is correct
- `LiveCanvas.AgentHost` can be driven as a real child process
- bridge JSON-RPC request/response flow is viable
- the current tool surface can be exercised end-to-end against a mock Rhino-side bridge

The current harness does not prove:

- real Rhino 8 plugin loading
- real Grasshopper document mutation
- real solve behavior in Rhino
- real viewport preview capture
- real `.gh` document save

## 6. Saved Spec

The implementation spec to use for the next step is:

- `doc/2026-03-17-live-rhino-harness-spec.md`

Review reconciliation already incorporated into that spec:

- live validation must pass on both macOS and Windows
- live preflight must not create the Grasshopper document before the MCP and AgentHost chain begins
- live warnings are recorded, but only fail the run if they block geometry, artifacts, or coincide with explicit errors

## 7. Exact Next Step

Next implementation step:

- implement `--mode live` in `tools/LiveCanvas.SmokeHarness` using the saved spec

Concretely:

1. keep `mock` mode as the default CI-safe baseline
2. add `live` mode that attaches to `ws://127.0.0.1:17881/livecanvas/v0` by default
3. keep the same smoke graph and MCP sequence in both modes
4. add direct live-bridge preflight using `gh_session_info` only
5. move first document creation to the MCP and `AgentHost` path
6. add manifest and transcript output
7. add opt-in live tests and manual live smoke procedure

## 8. Notes For The Next Machine

- Continue on the same branch unless there is a deliberate reason to branch again.
- Preserve the current smoke harness WIP; it is the baseline for the live upgrade.
- Do not change the public MCP tool surface as part of the harness upgrade.
- If live implementation reveals a product-level contract problem, record it separately instead of silently extending the runtime.
