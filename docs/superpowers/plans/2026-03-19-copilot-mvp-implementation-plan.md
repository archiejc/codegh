# Copilot MVP 内置模型版实施计划

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `LiveCanvas.AgentHost` 中新增一个最小可用的内置 copilot，支持 `文本 + 本地图片` 输入，先产出结构化执行计划，再执行到 Rhino/Grasshopper 并落地产物。

**Architecture:** `RhinoPlugin` 继续只负责 localhost bridge，不引入模型依赖。`AgentHost` 负责模型调用、brief 归一化、模板选型、模板参数化、repair orchestration，并通过现有 `gh_*` handlers 执行 `copilot_apply_plan`。`copilot_plan` 必须是 host-only 可运行路径，`copilot_apply_plan` 继续依赖 live Rhino/Grasshopper bridge。

**Tech Stack:** .NET 8, MCP stdio, existing `gh_*` tool handlers, Rhino localhost WebSocket bridge, OpenAI-compatible HTTP provider, existing `ReferenceBrief` / `ReferenceBriefSimplifier` / `TemplatePlanner` / `RepairEngine`

---

## Summary

- 新增两个公开 MCP tools：`copilot_plan` 和 `copilot_apply_plan`，它们正式进入 `tools/list`，并同步更新现有 tool-surface tests 与 SmokeHarness 预期。
- MVP 仍是严格的 `plan/apply` 两步：`copilot_plan` 负责多模态理解并返回完整 `execution_plan`；`copilot_apply_plan` 只执行该计划，不再调用模型。
- `execution_plan` 在 v1 中是“server-emitted only” 的公共 payload：`copilot_apply_plan` 只保证接受同 schema 版本的 `copilot_plan` 原生输出，不承诺人工编辑兼容性。
- MVP 必须让“尺寸 + 结构提示”真实影响结果：至少让 `ApproxDimensions`、`PodiumHeight`、`TowerHeight`、`StepCount`、`TaperRatio` 进入生成图配置；`StyleHints`、`RotationDegrees`、`OffsetPattern` 在 v1 中明确保留但不驱动输出。
- copilot 的端到端验证并入现有 `LiveCanvas.SmokeHarness` 主线，不另起平行 E2E 叙事。

## Fixed Contracts

### MCP Tool Surface Contract

- `src/LiveCanvas.AgentHost/Mcp/ToolDefinitions.cs` 改为 MCP public surface 的唯一事实来源，不再直接映射 `BridgeMethodNames.All`。
- `src/LiveCanvas.Bridge.Protocol/BridgeMethodNames.cs` 继续只定义 Rhino bridge JSON-RPC methods，不加入 `copilot_*`。
- `tools/list` 公开顺序固定为：
  - `gh_session_info`
  - `gh_new_document`
  - `gh_list_allowed_components`
  - `gh_add_component`
  - `gh_configure_component`
  - `gh_connect`
  - `gh_delete_component`
  - `gh_solve`
  - `gh_inspect_document`
  - `gh_capture_preview`
  - `gh_save_document`
  - `copilot_plan`
  - `copilot_apply_plan`

### `copilot_plan` Contract

- 输入：
  - `prompt: string` 必填
  - `image_paths: string[]` 可选，最多 4 张，只接受绝对本地路径，扩展名限 `.png` / `.jpg` / `.jpeg`
- 行为：
  - 在任何模型调用前完成路径、数量、扩展名校验
  - 将图片转成 data URL，调用单个 OpenAI-compatible multimodal model
  - 要求模型返回可直接反序列化为 `ReferenceBrief` 草稿的单个 JSON 对象
  - 经 `ReferenceBriefSimplifier` 归一化后，交给 `TemplatePlanner` 产出拓扑，再交给新增的参数化器填充全部 `GhComponentConfig`
  - 不触发 Rhino bridge
- 输出：
  - `execution_plan`

### `execution_plan` Contract

- 新增 `src/LiveCanvas.Contracts/Copilot/CopilotModels.cs`，定义以下 payload：
  - `CopilotExecutionPlan`
    - `schema_version = "copilot_execution_plan/v1"`
    - `input_prompt`
    - `input_images`
    - `reference_brief`
    - `template_name`
    - `graph_plan`
    - `assumptions`
    - `warnings`
    - `suggested_document_name`
- `graph_plan` 直接复用 `TemplateGraphPlan`，但要求其中每个 `GraphComponentPlan.Config` 已是参数化后的最终配置，不允许 apply 阶段再推导 brief 映射。
- v1 支持策略：
  - `copilot_apply_plan` 仅保证接受 `schema_version == "copilot_execution_plan/v1"` 的 payload
  - 仅保证接受由同一产品线的 `copilot_plan` 生成的计划
  - 不做签名或防篡改能力
  - 人工编辑后的 payload 视为 unsupported input，只做 schema + semantic validation，不承诺兼容

### `copilot_apply_plan` Contract

- 输入：
  - `execution_plan` 必填
  - `output_dir` 可选，缺省时写入临时目录
  - `preview_width` 可选，默认 `1600`
  - `preview_height` 可选，默认 `900`
  - `expire_all` 可选，默认 `true`
- 固定产物：
  - `preview.png`
  - `document.gh`
- 执行前校验：
  - `schema_version` 匹配
  - `template_name == graph_plan.template_name`
  - component aliases 唯一
  - component keys 全部在 whitelist 中
  - configs 能通过现有 `ComponentConfigValidator`
  - connections 中所有 aliases 都存在，port names 能通过现有 `ConnectionValidator`
- 输出：
  - `status`：`succeeded` / `failed` / `repair_exhausted`
  - `repair_iterations`
  - `repair_actions`
  - `new_document`
  - `solve`
  - `inspect`
  - `preview_path`
  - `document_path`
  - `warnings`
- 结果语义：
  - `new_document` / `solve` / `inspect` 报告最终成功尝试，若全部失败则报告最后一次尝试
  - `repair_actions` 聚合全部尝试中的 repair action，按时间顺序返回
  - `preview_path` 与 `document_path` 只有在最终尝试实际写出对应文件时才返回绝对路径，否则返回 `null`
  - 每次重试前先删除 `output_dir` 中已有的 `preview.png` / `document.gh`，禁止把旧尝试产物冒充为最终结果

## Brief-to-Config Mapping

- `single_extrusion`
  - `width slider = ApproxDimensions.Width`
  - `depth slider = ApproxDimensions.Depth`
  - `height slider = ApproxDimensions.Height`
- `lofted_taper`
  - `base_width = ApproxDimensions.Width`
  - `base_depth = ApproxDimensions.Depth`
  - `top_width = clamp(width * taperRatio, min, width)`，`taperRatio` 缺省 `0.7`
  - `top_depth = clamp(depth * taperRatio, min, depth)`，`taperRatio` 缺省 `0.7`
  - `height = ApproxDimensions.Height`
- `podium_tower`
  - 生成两个 extrusion subgraphs：`podium` 与 `tower`
  - `podium_height = clamp(Leveling.PodiumHeight ?? height * 0.2, MinHeight, height * 0.5)`
  - `tower_height = clamp(Leveling.TowerHeight ?? (height - podium_height), MinHeight, MaxHeight)`
  - `tower_width = clamp(width * 0.6, MinWidthOrDepth, width)`
  - `tower_depth = clamp(depth * 0.6, MinWidthOrDepth, depth)`
  - `tower` 底面整体上移 `podium_height`
- `stepped_extrusions`
  - `tier_count = clamp(Leveling.StepCount ?? 3, 2, 5)`
  - 生成 `tier_count` 个 extrusion tiers
  - 每层 `tier_height = height / tier_count`
  - 每升一层，`width` 与 `depth` 各缩减上一层的 `10%`
  - 每层整体上移前序层累计高度
- `stacked_bars`
  - 生成 3 个 extrusion bars，作为 `loft_failure` 的固定回退模板
  - 三段高度比例固定为 `0.45 / 0.35 / 0.20`
  - 第二、三段分别在 XY 平面做固定偏移：`(+width * 0.12, +depth * 0.08)` 与 `(-width * 0.10, +depth * 0.10)`
- `colour_swatch`
  - v1 固定使用默认白色，不从 `StyleHints.Color` 驱动
- v1 明确忽略：
  - `StyleHints.Color`
  - `StyleHints.Silhouette`
  - `TransformHints.RotationDegrees`
  - `TransformHints.OffsetPattern`
- oversized raw dimensions 处理规则：
  - `copilot_plan` 阶段必须先经 `ReferenceBriefSimplifier` clamp 到合法维度，再进入参数化
  - 如果模型草稿维度被 clamp，`execution_plan.warnings` 必须包含 `dimensions_clamped`
  - `copilot_apply_plan` v1 不再把 `oversized_dimensions` 作为 repair 分类，因为 server-emitted plan 在 apply 前已经完成维度归一化

## Repair Semantics

- 每次 repair 都必须：
  - 新建全新 GH 文档
  - 重新执行 add/configure/connect/solve/inspect/capture/save 全链路
  - 不在原图上做局部补丁
- 失败分类与变更规则固定为：
  - `invalid_connection`
    - 触发条件：执行前 semantic validation 发现别名或 port 非法，或 bridge/tool 返回明确非法连接错误
    - 变更：对当前 `reference_brief` 重新运行 planner + parameterizer，替换整份 `graph_plan`
  - `loft_failure`
    - 触发条件：当前 template 为 `lofted_taper`，`gh_solve` 成功但 `inspect.previewSummary.hasGeometry == false`
    - 变更：切换 template 到 `stacked_bars`，重新 planner + parameterizer
  - `subgraph_failure`
    - 触发条件：任何 add/configure/connect/solve/inspect/capture/save 失败，但没有更具体分类
    - 变更：保持当前 template，不改 brief，完整重建同一份规划
- 最大 repair budget 为 3 次，复用 `RepairEngine.MaxIterations`
- v1 不做 runtime message NLP，不新增更多 failure kinds

## File Map

- Modify: `src/LiveCanvas.AgentHost/Startup/Program.cs`
- Create: `src/LiveCanvas.AgentHost/Startup/AgentHostCompositionRoot.cs`
- Modify: `src/LiveCanvas.AgentHost/Mcp/McpToolExecutor.cs`
- Modify: `src/LiveCanvas.AgentHost/Mcp/McpToolCatalog.cs`
- Modify: `src/LiveCanvas.AgentHost/Mcp/ToolDefinitions.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/CopilotOptions.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/ICopilotModelClient.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/OpenAiCompatibleCopilotModelClient.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/ICopilotPlanService.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/CopilotPlanService.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/ICopilotApplyService.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/CopilotApplyService.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/CopilotExecutionPlanValidator.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/CopilotFailureClassifier.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/BridgeGraphExecutor.cs`
- Create: `src/LiveCanvas.Contracts/Copilot/CopilotModels.cs`
- Create: `src/LiveCanvas.Core/Planner/TemplateGraphParameterizer.cs`
- Modify: `src/LiveCanvas.Core/Planner/TemplatePlanner.cs`
- Modify: `tests/LiveCanvas.AgentHost.Tests/McpToolSurfaceTests.cs`
- Create: `tests/LiveCanvas.AgentHost.Tests/CopilotPlanToolTests.cs`
- Create: `tests/LiveCanvas.AgentHost.Tests/CopilotApplyToolTests.cs`
- Create: `tests/LiveCanvas.Contracts.Tests/CopilotContractSerializationTests.cs`
- Create: `tests/LiveCanvas.Core.Tests/Planner/TemplateGraphParameterizerTests.cs`
- Modify: `tests/LiveCanvas.Core.Tests/Planner/TemplatePlannerTests.cs`
- Create: `tools/LiveCanvas.SmokeHarness/MockCopilotProviderServer.cs`
- Modify: `tools/LiveCanvas.SmokeHarness/SmokeHarnessModels.cs`
- Modify: `tools/LiveCanvas.SmokeHarness/SmokeHarnessRunner.cs`
- Modify: `tools/LiveCanvas.SmokeHarness/SmokeHarnessCli.cs`
- Modify: `tests/LiveCanvas.E2E.Tests/SmokeHarnessRunnerTests.cs`
- Modify: `tests/LiveCanvas.E2E.Tests/SmokeHarnessCliTests.cs`
- Modify: `README.md`

## Chunk 1: Public Contracts and Composition Root

### Task 1: Split MCP public surface from bridge method names

**Files:**
- Modify: `tests/LiveCanvas.AgentHost.Tests/McpToolSurfaceTests.cs`
- Modify: `src/LiveCanvas.AgentHost/Mcp/ToolDefinitions.cs`
- Modify: `src/LiveCanvas.AgentHost/Mcp/McpToolCatalog.cs`

- [ ] Write failing tests so `ToolDefinitions.All` expects the 11 existing `gh_*` tools plus `copilot_plan` and `copilot_apply_plan`, in that exact order.
- [ ] Run: `dotnet test tests/LiveCanvas.AgentHost.Tests/LiveCanvas.AgentHost.Tests.csproj --filter McpToolSurfaceTests -v minimal`
- [ ] Update `ToolDefinitions.All` to own the MCP surface directly instead of forwarding `BridgeMethodNames.All`.
- [ ] Append `copilot_plan` and `copilot_apply_plan` descriptors to `McpToolCatalog.All`.
- [ ] Re-run the same test until green.

### Task 2: Add a composition root that can build both legacy handlers and copilot services

**Files:**
- Modify: `src/LiveCanvas.AgentHost/Startup/Program.cs`
- Create: `src/LiveCanvas.AgentHost/Startup/AgentHostCompositionRoot.cs`
- Modify: `src/LiveCanvas.AgentHost/Mcp/McpToolExecutor.cs`

- [ ] Write a failing test in `tests/LiveCanvas.AgentHost.Tests/CopilotPlanToolTests.cs` that constructs `McpToolExecutor` with fake dependencies and asserts `copilot_plan` can be routed without creating a bridge client.
- [ ] Run: `dotnet test tests/LiveCanvas.AgentHost.Tests/LiveCanvas.AgentHost.Tests.csproj --filter CopilotPlanToolTests -v minimal`
- [ ] Add `AgentHostCompositionRoot` to construct registry, validators, session state, bridge client, legacy handlers, and copilot services in one place.
- [ ] Change `Program.cs` to instantiate the server via the composition root.
- [ ] Refactor `McpToolExecutor` to accept dependencies via constructor injection and to route `copilot_plan` / `copilot_apply_plan`.
- [ ] Re-run the targeted test until green.

## Chunk 2: Copilot Planning Path

### Task 3: Define copilot contracts and environment configuration

**Files:**
- Create: `src/LiveCanvas.Contracts/Copilot/CopilotModels.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/CopilotOptions.cs`
- Create: `tests/LiveCanvas.Contracts.Tests/CopilotContractSerializationTests.cs`

- [ ] Write failing serialization tests for `CopilotExecutionPlan`, `CopilotPlanRequest`, `CopilotPlanResponse`, `CopilotApplyPlanRequest`, and `CopilotApplyPlanResponse`.
- [ ] Run: `dotnet test tests/LiveCanvas.Contracts.Tests/LiveCanvas.Contracts.Tests.csproj --filter CopilotContractSerializationTests -v minimal`
- [ ] Implement the new contract models with camelCase-friendly records and `schema_version = "copilot_execution_plan/v1"`.
- [ ] Implement `CopilotOptions` so missing `LIVECANVAS_COPILOT_BASE_URL`, `LIVECANVAS_COPILOT_API_KEY`, or `LIVECANVAS_COPILOT_MODEL` produces a clear configuration error for `copilot_plan`.
- [ ] Re-run the targeted contract tests until green.

### Task 4: Implement model client and plan service

**Files:**
- Create: `src/LiveCanvas.AgentHost/Copilot/ICopilotModelClient.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/OpenAiCompatibleCopilotModelClient.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/ICopilotPlanService.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/CopilotPlanService.cs`
- Create: `tests/LiveCanvas.AgentHost.Tests/CopilotPlanToolTests.cs`

- [ ] Add failing tests for:
  - bad image path rejection before provider call
  - too many images rejection before provider call
  - invalid model JSON rejection before any bridge call
  - oversized model dimensions are clamped during plan generation and surface `dimensions_clamped`
  - successful plan generation returning fully populated `execution_plan`
- [ ] Run: `dotnet test tests/LiveCanvas.AgentHost.Tests/LiveCanvas.AgentHost.Tests.csproj --filter CopilotPlanToolTests -v minimal`
- [ ] Implement the provider client against `POST /chat/completions`, with text + data URL image parts in one request.
- [ ] Implement `CopilotPlanService` so it:
  - validates inputs
  - reads images
  - calls the model client
  - deserializes to `ReferenceBrief`
  - simplifies the brief
  - calls the planner and parameterizer
  - returns `execution_plan`
- [ ] Generate `suggested_document_name` by slugifying the prompt prefix and falling back to `livecanvas-{template_name}`.
- [ ] Re-run the targeted tests until green.

### Task 5: Parameterize graph plans so dimensions and structure hints affect output

**Files:**
- Create: `src/LiveCanvas.Core/Planner/TemplateGraphParameterizer.cs`
- Modify: `src/LiveCanvas.Core/Planner/TemplatePlanner.cs`
- Modify: `tests/LiveCanvas.Core.Tests/Planner/TemplatePlannerTests.cs`
- Create: `tests/LiveCanvas.Core.Tests/Planner/TemplateGraphParameterizerTests.cs`

- [ ] Write failing planner tests proving:
  - `podium_tower`, `stepped_extrusions`, and `stacked_bars` now emit distinct topologies
  - all emitted component keys remain whitelisted
  - `lofted_taper` still emits a loft topology
- [ ] Write failing parameterizer tests proving:
  - single extrusion sliders receive exact normalized width/depth/height
  - podium/tower heights split correctly
  - stepped extrusion tier count follows `StepCount`
  - tapered top dimensions follow `TaperRatio`
  - color/style fields are ignored in v1
- [ ] Run: `dotnet test tests/LiveCanvas.Core.Tests/LiveCanvas.Core.Tests.csproj --filter \"TemplatePlannerTests|TemplateGraphParameterizerTests\" -v minimal`
- [ ] Implement distinct planners for `podium_tower`, `stepped_extrusions`, and `stacked_bars`.
- [ ] Implement `TemplateGraphParameterizer` so every returned `GraphComponentPlan.Config` is final and apply-ready.
- [ ] Re-run the targeted core tests until green.

## Chunk 3: Apply Path and Repair Loop

### Task 6: Validate and execute execution plans without calling the model

**Files:**
- Create: `src/LiveCanvas.AgentHost/Copilot/CopilotExecutionPlanValidator.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/BridgeGraphExecutor.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/ICopilotApplyService.cs`
- Create: `src/LiveCanvas.AgentHost/Copilot/CopilotApplyService.cs`
- Create: `tests/LiveCanvas.AgentHost.Tests/CopilotApplyToolTests.cs`

- [ ] Write failing tests for:
  - schema version mismatch
  - alias mismatch / missing alias / bad port validation before bridge call
  - successful apply executes `gh_new_document -> add -> configure -> connect -> solve -> inspect -> capture -> save`
  - apply never calls the model client
  - failed capture/save returns `preview_path = null` and/or `document_path = null` instead of stale file paths
- [ ] Run: `dotnet test tests/LiveCanvas.AgentHost.Tests/LiveCanvas.AgentHost.Tests.csproj --filter CopilotApplyToolTests -v minimal`
- [ ] Implement `CopilotExecutionPlanValidator` using existing whitelist and config/connection validators.
- [ ] Implement `BridgeGraphExecutor` to reuse existing tool handlers rather than recursive JSON MCP calls.
- [ ] Implement `CopilotApplyService` to execute the validated plan, write `preview.png` and `document.gh`, and shape the response contract.
- [ ] Re-run the targeted tests until green.

### Task 7: Make repair behavior deterministic and testable

**Files:**
- Create: `src/LiveCanvas.AgentHost/Copilot/CopilotFailureClassifier.cs`
- Modify: `tests/LiveCanvas.AgentHost.Tests/CopilotApplyToolTests.cs`

- [ ] Write failing tests for:
  - `loft_failure` switches to `stacked_bars`
  - `subgraph_failure` rebuilds the same template from scratch
  - repair budget exhaustion returns `repair_exhausted`
- [ ] Run: `dotnet test tests/LiveCanvas.AgentHost.Tests/LiveCanvas.AgentHost.Tests.csproj --filter CopilotApplyToolTests -v minimal`
- [ ] Implement `CopilotFailureClassifier` with the exact three failure kinds defined above.
- [ ] Reuse the existing `RepairEngine` behavior for `invalid_connection` / `loft_failure` / `subgraph_failure`, but move plan mutation into `CopilotApplyService` so each retry regenerates a full new `graph_plan`.
- [ ] Re-run the targeted apply test command until green.

## Chunk 4: Smoke Harness, E2E, and Docs

### Task 8: Extend the canonical smoke harness with a copilot scenario

**Files:**
- Create: `tools/LiveCanvas.SmokeHarness/MockCopilotProviderServer.cs`
- Modify: `tools/LiveCanvas.SmokeHarness/SmokeHarnessModels.cs`
- Modify: `tools/LiveCanvas.SmokeHarness/SmokeHarnessRunner.cs`
- Modify: `tools/LiveCanvas.SmokeHarness/SmokeHarnessCli.cs`
- Modify: `tests/LiveCanvas.E2E.Tests/SmokeHarnessRunnerTests.cs`
- Modify: `tests/LiveCanvas.E2E.Tests/SmokeHarnessCliTests.cs`

- [ ] Add failing CLI tests for a new scenario named `copilot-absolute-towers`.
- [ ] Add failing runner tests proving the harness can:
  - start a mock copilot provider
  - call `copilot_plan`
  - call `copilot_apply_plan`
  - assert `preview.png` and `document.gh`
  - keep using the same manifest/transcript structure
- [ ] Run: `dotnet test tests/LiveCanvas.E2E.Tests/LiveCanvas.E2E.Tests.csproj --filter \"SmokeHarnessCliTests|SmokeHarnessRunnerTests\" -v minimal`
- [ ] Update the harness so raw `gh_*` scenarios still compare against the full public `ToolDefinitions.All`.
- [ ] Add the copilot scenario to the harness instead of creating a separate E2E runner.
- [ ] Use `MockCopilotProviderServer` in mock mode so tests never call a real model provider.
- [ ] Re-run the targeted E2E tests until green.

### Task 9: Update README and manual validation flow

**Files:**
- Modify: `README.md`

- [ ] Update README to document:
  - new `copilot_plan` and `copilot_apply_plan` tools
  - env vars for copilot provider config
  - image path constraints
  - `copilot_plan` host-only support on Linux/macOS/Windows
  - `copilot_apply_plan` live-bridge requirement
  - example MCP calls for `plan -> apply`
  - current limitations: new document only, no session memory, no existing definition edit, no third-party plugins, no editable execution plans
- [ ] Add a manual live acceptance checklist for:
  - one text prompt
  - one local image
  - successful `copilot_plan`
  - successful `copilot_apply_plan`
  - visible Grasshopper canvas mutation
  - saved `preview.png` and `document.gh`

## Verification Commands

- `dotnet test tests/LiveCanvas.Contracts.Tests/LiveCanvas.Contracts.Tests.csproj -v minimal`
- `dotnet test tests/LiveCanvas.Core.Tests/LiveCanvas.Core.Tests.csproj -v minimal`
- `dotnet test tests/LiveCanvas.AgentHost.Tests/LiveCanvas.AgentHost.Tests.csproj -v minimal`
- `dotnet test tests/LiveCanvas.E2E.Tests/LiveCanvas.E2E.Tests.csproj -v minimal`
- `dotnet test LiveCanvas.sln -v minimal`
- Manual live smoke:
  - `dotnet run --project tools/LiveCanvas.SmokeHarness/LiveCanvas.SmokeHarness.csproj -- --mode live --scenario copilot-absolute-towers`

## Assumptions

- 第一版用户面只有 MCP tools，不做 Rhino 内 widget 或独立 chat UI。
- `copilot_plan` 是 host-only capable；`copilot_apply_plan` 继续要求 Rhino bridge 可用。
- `execution_plan` 在 v1 中是公开传输格式，但不是可编辑产品表面，不承诺第三方手写 payload 兼容性。
- 模型 provider 固定为 OpenAI-compatible `chat/completions`；Azure / Responses API / provider-specific features 不进入 MVP。
- v1 的视觉风格控制只保留在 brief 中，不驱动输出；真正参与输出的是尺寸和结构提示。
- repair loop 是有界的 shallow repair，不做局部图补丁，也不做复杂错误语义理解。
