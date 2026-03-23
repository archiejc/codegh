# Extensible Grasshopper Component Configuration Protocol (Proposal)

Date: 2026-03-22
Owner: Agent 2 (design only)
Scope: contracts + runtime protocol shape for configuring Grasshopper components from copilot plans.

## Goal

Enable copilot to *use* and eventually *learn to use* a large set of Grasshopper components by making "component configuration" extensible, discoverable, and safe to execute.

In practice, this means we need a protocol that can:

- Express common configuration in a uniform way (e.g., set nickname, set default inputs).
- Provide component-specific configuration for special UI/value components (sliders, panels, toggles, value lists, etc.).
- Validate requests deterministically (fail fast where possible).
- Remain backward compatible with existing `gh_configure_component` calls and `GhComponentConfig`.

Non-goals (for the first iteration):

- Full universal, automatic, lossless editing of every GH component's hidden settings and menu states.
- Arbitrary code execution via script components.
- Supporting every third-party plugin component without a per-plugin story.

## Current State (What We Have Today)

### Current config model

`src/LiveCanvas.Contracts/Components/GhComponentModels.cs` defines:

- `GhComponentConfig` with fields:
  - `nickname: string?`
  - `slider: SliderConfig?`
  - `panel: PanelConfig?`
  - `colour: ColourSwatchConfig?`

### Current runtime application behavior

In `src/LiveCanvas.RhinoPlugin/Runtime/LiveCanvasRuntime.cs`, `ConfigureComponent`:

- Always applies `nickname` to `IGH_DocumentObject.NickName` when present.
- Applies `slider/panel/colour` only for:
  - `Number Slider` (`GH_NumberSlider`)
  - `Panel` (`GH_Panel`)
  - `Colour Swatch` (`GH_ColourSwatch`)
- For all other components, config effectively reduces to "nickname only".

### Current validation behavior

`src/LiveCanvas.Core/Validation/ComponentConfigValidator.cs`:

- Normalizes only those three specialized configs.
- Otherwise returns `new GhComponentConfig(Nickname: config.Nickname)`.

### Why this blocks "copilot will use all components"

Many GH components are primarily controlled via:

1. Wiring inputs (already supported by `gh_connect`).
2. Setting input "persistent data" (default values when no wire is connected).
3. Adjusting component-specific settings (e.g., Boolean Toggle state, Value List options/selection, Graph Mapper curve, "Flatten/Graft/Simplify" on params, etc.).

Today we only have (2) for *three* special components, and no general representation for (2) and (3).

## Requirements for Broad Component Support

### R1: A generic way to set default input values

This is the biggest lever. If we can set "persistent data" on a component input parameter, copilot can drive many components without needing to create and configure separate source components every time.

Example: if a component input `R` expects a number and has no incoming wire, setting persistent data to `10.0` is equivalent to feeding it from a constant number.

### R2: A clean path for component-specific adapters

Some components cannot be configured meaningfully with only nickname + persistent inputs:

- UI source components: Slider, Panel, Boolean Toggle, Button, Value List, etc.
- Components with special internal modes (e.g., curve division modes, remap toggles).
- Param-level flags: flatten/graft/simplify, access mode, optional input toggles.

We need a protocol that can add a new adapter without changing the base schema.

### R3: Discoverability and validation

Copilot needs to know what it *can* configure, and the runtime needs to decide what it *will* accept.

At minimum:

- Describe which config operations are supported for a given component.
- Validate port names and basic value shapes.
- Provide deterministic error messages when unsupported (so copilot can self-repair).

### R4: Backward compatibility

Existing tool surface and plans should not break.

Strategy: keep existing `gh_configure_component` and add `gh_configure_component_v2` (or add an optional `schema_version` field) so old clients still work.

## Proposed Protocol (v2)

### High-level concept

Replace the fixed `GhComponentConfig` shape with an "operation list" (patch-like protocol).

- Base protocol supports common operations: set nickname, set persistent input data.
- Component-specific features are expressed as `adapter_*` operations and only accepted when the runtime has a matching adapter.

This avoids a schema explosion of hundreds of optional fields.

### Suggested JSON shape

New request:

```json
{
  "component_id": "0123abcd...",
  "schema_version": "gh_component_config/v2",
  "ops": [
    { "kind": "set_nickname", "value": "height" },
    {
      "kind": "set_input_persistent_data",
      "input": "R",
      "value": { "type": "number", "value": 12.5 }
    }
  ]
}
```

Suggested response (always includes a normalized representation the runtime applied):

```json
{
  "component_id": "0123abcd...",
  "applied": true,
  "normalized": {
    "schema_version": "gh_component_config/v2",
    "ops": [
      { "kind": "set_nickname", "value": "height" },
      {
        "kind": "set_input_persistent_data",
        "input": "R",
        "value": { "type": "number", "value": 12.5 }
      }
    ]
  },
  "warnings": []
}
```

### Operation kinds (MVP set)

1. `set_nickname`

- Applies to `IGH_DocumentObject.NickName`.

2. `set_input_persistent_data`

- Applies to a target input port (by canonical port name).
- Sets/overwrites the persistent data on the input parameter.
- Fails if the input does not exist.

3. `clear_input_persistent_data`

- Clears persistent data on that input parameter.

4. `set_param_flags` (optional for v2 MVP; strongly recommended early)

- Targets either an input or output parameter on the component by name.
- Allows setting:
  - `flatten`, `graft`, `simplify`
  - `reverse` (for some param types)
  - `access` (item/list/tree) when supported

This has high leverage and is more generic than per-component adapters.

5. `adapter_config`

- For component-specific behavior that cannot be expressed generically.
- Payload is adapter-defined but must be validated by that adapter.

Example for slider:

```json
{
  "kind": "adapter_config",
  "adapter": "number_slider",
  "value": { "min": 0, "max": 100, "value": 80, "integer": false }
}
```

### Value encoding (`GhValue`)

For `set_input_persistent_data`, define a small, explicit union to avoid ambiguous "JSON means GH" casting.

MVP recommended types:

- `number` (double)
- `integer` (int)
- `boolean` (bool)
- `string` (string)
- `point3d` (`{x,y,z}`)
- `vector3d` (`{x,y,z}`)
- `color` (`{r,g,b,a}`)
- `list` (`{ items: GhValue[] }`) to support list inputs without extra components

Future:

- `plane`, `domain`, `interval`, `line`, `polyline`, `curve`, `brep` (likely needs a stable geometry serialization story)

Important: the runtime should reject unknown `type` values unless a feature flag enables permissive casting.

## Runtime Adapter Model

### Concept

Implement a registry of configurators:

- A *generic configurator* handles:
  - `set_nickname`
  - `set_input_persistent_data` / `clear_input_persistent_data`
  - `set_param_flags` (if included)
- Specialized configurators handle `adapter_config` for specific component keys/types.

### Matching strategy

Adapters should match by:

1. Component key (preferred when keys are stable).
2. Emitted object runtime type (fallback when keys are discovered dynamically).

### Adapter responsibilities

Each adapter should:

- Advertise supported ops and their schemas (for discoverability).
- Validate inputs (type, range, required fields).
- Apply changes deterministically and return a normalized applied payload.
- Return warnings rather than failing when it can safely auto-normalize (e.g., swap min/max).

## Validation Strategy

Validation should occur in two stages:

1. Host-side (AgentHost) validation before sending over bridge:
  - Schema version matches.
  - `component_id` is present.
  - Op list is non-empty (optional).
  - For ops that reference port names, ensure the port exists using the component definition cache (if available), otherwise defer to runtime.

2. Runtime (Rhino plugin) validation:
  - Resolve the component instance.
  - Resolve the target input param(s) by canonical port name.
  - Validate `GhValue` type is supported for that parameter.
  - For adapter ops, ensure adapter exists; otherwise fail with a structured error.

Error design requirement:

- Errors should be classified (e.g., `unsupported_op`, `invalid_port`, `invalid_value_type`), so copilot can repair.

## Discoverability (How Copilot Knows What It Can Configure)

Today `gh_list_allowed_components` returns `ConfigFields` in `AllowedComponentDefinition`.

For v2, evolve this to a richer "config capabilities" object. Options:

Option A: Replace `ConfigFields` with a structured `ConfigCapabilities`.
Option B: Keep `ConfigFields` for v1 and add `ConfigOps` for v2.

Recommended:

- Keep `ConfigFields` for backward compatibility.
- Add `ConfigOps` (list of op descriptors) for v2 clients.

Example (conceptual):

```json
{
  "component_key": "some_component",
  "display_name": "Area",
  "inputs": [...],
  "outputs": [...],
  "config_ops": [
    { "kind": "set_nickname" },
    { "kind": "set_input_persistent_data", "supported_value_types": ["number","integer","string", ...] },
    { "kind": "set_param_flags", "supported_flags": ["flatten","graft","simplify"] }
  ]
}
```

## Backward Compatibility Plan

1. Keep current `gh_configure_component` and `GhComponentConfig` as v1.
2. Introduce `gh_configure_component_v2` and `GhConfigureComponentV2Request/Response`.
3. Internally, treat v1 as a subset of v2:
  - `nickname` -> `set_nickname`
  - `slider/panel/colour` -> `adapter_config` with adapter names `number_slider/panel/colour_swatch`

This allows:

- Existing tests and clients to keep working.
- Copilot planner to migrate to v2 gradually.

## Suggested MVP Scope for Agent 3 (Implementation Guidance)

To unlock broad component usage fast:

- Implement v2 with these ops:
  - `set_nickname`
  - `set_input_persistent_data` and `clear_input_persistent_data`
  - `adapter_config` for slider/panel/colour swatch (ported from v1)
- Add minimal `GhValue` types: number/integer/boolean/string + point3d/vector3d + list
- Provide clear error codes for invalid ports and unsupported types.

This already enables copilot to drive many computational components without extra source components.

## Open Questions for User Confirmation

1. Should copilot be allowed to set persistent data on *any* component input param, even if it changes behavior when the param also has sources connected?
   Proposed default: if a wire exists, either reject or keep persistent data but it will be ignored by GH; choose deterministic behavior.

2. Do we need to support parameter flags (flatten/graft/simplify) in the MVP?
   It is generic and extremely powerful for GH workflows, but increases surface area.

3. Geometry serialization: do you want copilot to set geometry-typed persistent inputs (curves/surfaces/breps) directly, or is it acceptable to require geometry to come from other components (e.g., constructed in-graph)?
   Proposed staged approach: start without direct geometry serialization.

4. Safety constraints: should we categorically block configuration of script components (C#/Python) and components that can touch filesystem/network?
   Proposed default: block or require explicit opt-in.

5. Value List / Toggle / Button priority:
   For "copilot feels real", these are important. Confirm if we should prioritize adapters for these next after v2 MVP.

## Appendix: Suggested Tool/Method Naming

To avoid breaking v1 clients:

- Keep existing JSON-RPC method: `gh_configure_component` (v1 payload: `GhConfigureComponentRequest` with `GhComponentConfig`).
- Add a new JSON-RPC method: `gh_configure_component_v2`.

Suggested contracts naming (C#):

- `GhConfigureComponentV2Request`:
  - `string ComponentId`
  - `string SchemaVersion` (must be `gh_component_config/v2`)
  - `IReadOnlyList<GhComponentConfigOp> Ops`
- `GhConfigureComponentV2Response`:
  - `string ComponentId`
  - `bool Applied`
  - `GhComponentConfigV2 Normalized`
  - `IReadOnlyList<string> Warnings`
