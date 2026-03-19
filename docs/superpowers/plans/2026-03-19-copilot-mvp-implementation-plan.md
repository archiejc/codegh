# Copilot MVP 内置模型版实施计划

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把当前低层 `gh_*` runtime 提升为一个最小可用的内置 copilot，支持文本和本地图片输入，在 `AgentHost` 内直接调用模型生成并执行原生 Grasshopper 图。

**Architecture:** 保持 `RhinoPlugin` 无模型依赖，把模型调用、brief 归一化、模板规划、repair 编排都放在 `LiveCanvas.AgentHost`。对外暴露 `copilot_plan` / `copilot_apply_plan` 两个 MCP 工具，前者只做多模态理解和计划生成，后者只做 Rhino/Grasshopper 执行与产物落地。

**Tech Stack:** .NET 8, MCP stdio, Rhino localhost WebSocket bridge, OpenAI-compatible HTTP provider, existing `ReferenceBrief` / `TemplatePlanner` / `RepairEngine`

---

## Summary

- 目标是把当前“低层 `gh_*` runtime”提升为一个最小可用的内置 copilot：支持 `文本 + 本地图片` 输入，在 `AgentHost` 内直接调用模型，把输入转成结构化 brief 和模板化 GH 图，再通过现有 bridge 执行到 Rhino/Grasshopper。
- 第一版严格限定为 `plan/apply` 两步、`new document only`、`native component templates only`。不做已有定义编辑、不做 chat session、不做 widget、不做第三方插件接入。
- 模型集成放在 `LiveCanvas.AgentHost`，`RhinoPlugin` 保持无模型依赖。provider 采用 `OpenAI-compatible` 适配层，配置为环境变量，默认协议使用 `POST /chat/completions`。

## Public APIs / Interfaces

- 新增 MCP tools：
  - `copilot_plan`
    - 输入：`prompt`、`image_paths[]`
    - 约束：`image_paths` 仅接受绝对本地路径，扩展名限 `.png/.jpg/.jpeg`，最多 4 张
    - 行为：只做模型调用、brief 归一化、模板选型、图规划；不触发 Rhino bridge
    - 输出：`execution_plan`
      - `input_prompt`
      - `input_images`
      - `reference_brief`
      - `template_name`
      - `graph_plan`
      - `assumptions`
      - `warnings`
      - `suggested_document_name`
  - `copilot_apply_plan`
    - 输入：`execution_plan`、`output_dir`、`preview_width`、`preview_height`、`expire_all`
    - 默认：`output_dir` 缺省时写入临时目录；固定产出 `preview.png` 和 `document.gh`
    - 行为：新建文档、落图、solve、inspect、capture、save；不再调用模型
    - 输出：`status`、`repair_iterations`、`repair_actions`、`new_document`、`solve`、`inspect`、`preview_path`、`document_path`、`warnings`
- 新增配置环境变量：
  - `LIVECANVAS_COPILOT_BASE_URL`
  - `LIVECANVAS_COPILOT_API_KEY`
  - `LIVECANVAS_COPILOT_MODEL`
  - `LIVECANVAS_COPILOT_TIMEOUT_SECONDS`
- 新增内部接口：
  - `ICopilotModelClient`
  - `ICopilotPlanService`
  - `ICopilotApplyService`
- 现有 `gh_*` tool surface 保持兼容；新工具附加在现有 catalog 上，不改已有请求/响应形状。

## Key Changes

- `AgentHost` 先做一个最小 composition root 重构，不再在 `Program` 里直接 `new McpToolExecutor()` 后把所有依赖硬编码进去；改为集中构造 bridge client、validators、session state、tool handlers、copilot services。这样 `copilot_apply_plan` 可以直接复用现有 handlers，而不是递归走 JSON MCP 调用。
- `Contracts` 增加 copilot 专用 request/response records；`Core` 继续复用现有 `ReferenceBrief`、`ReferenceBriefSimplifier`、`TemplatePlanner`、`RepairEngine`，不为 MVP 再发明第二套 planner schema。
- `copilot_plan` 的模型调用策略固定为：
  - 用单个多模态模型同时处理文本和图片
  - `AgentHost` 将本地图片读成 data URL 后发送给 OpenAI-compatible provider
  - 要求模型返回单个 JSON 对象，字段直接可反序列化为一个 `brief draft`
  - draft 进入 `ReferenceBriefSimplifier` 后再做维度 clamp、模板选择与 assumptions 整理
  - `TemplatePlanner` 产出 `TemplateGraphPlan`，并打包成 `execution_plan`
- `copilot_apply_plan` 的执行策略固定为：
  - 总是先 `gh_new_document`
  - 按 `graph_plan` 顺序 add/configure/connect
  - 执行 `gh_solve` + `gh_inspect_document`
  - 一律 capture + save 到 `output_dir`
  - 失败时走有限 repair loop，最多 3 次，且每次都重建整张新文档，不做局部补丁
- MVP repair 分类固定为：
  - `LoftedTaper` 求解后无几何时映射为 `loft_failure`
  - 低层工具或 solve 失败但无更明确信号时映射为 `subgraph_failure`
  - 不做复杂 runtime message NLP 分类；已有 `invalid_connection` / `oversized_dimensions` 规则保留给显式已知场景
- 文档和 README 需要补充：
  - copilot env vars
  - `copilot_plan` / `copilot_apply_plan` 示例
  - 图片路径要求
  - 当前限制：新文档 only、无 session memory、无 existing definition edit、无 third-party plugins

## Test Plan

- `LiveCanvas.AgentHost.Tests`
  - `tools/list` 包含 `copilot_plan`、`copilot_apply_plan`
  - 缺失 `API_KEY` / `BASE_URL` / `MODEL` 时，`copilot_plan` 返回清晰配置错误
  - 非绝对图片路径、坏扩展名、超过 4 张图片时，在模型调用前失败
  - 模型返回非法 JSON 或缺字段时，在 bridge 调用前失败
  - `copilot_apply_plan` 在 fake bridge 下按顺序执行 new/add/configure/connect/solve/inspect/capture/save
  - `copilot_apply_plan` 不调用模型 client
- `LiveCanvas.Core.Tests`
  - 保持现有 simplifier/planner/repair tests
  - 新增 `execution_plan` 与 `TemplateGraphPlan` 的序列化/反序列化测试
  - 新增 repair loop 的 `loft_failure -> stacked_bars` 回退测试
- `LiveCanvas.E2E.Tests`
  - 新增一个 fake model client + mock bridge 的 copilot E2E：
    - `copilot_plan` 产出稳定 plan
    - `copilot_apply_plan` 产出 `document.gh` 和 `preview.png`
    - 返回的 `inspect.previewSummary.hasGeometry = true`
- 手工验收
  - 用一条文本 prompt 和一张参考图片跑完整 `plan -> apply`
  - 在真实 Rhino live 模式下确认文档创建、画布落图、solve、预览、保存全链路成功

## Assumptions

- 第一版用户面就是 MCP tools，不做 Rhino 内 widget 或独立 chat UI。
- 第一版是无状态请求模型：没有多轮 chat memory，没有 profile，没有 persistent context store。
- 第一版只支持创建新的 LiveCanvas-owned 文档；不载入、不修改已有 GH 定义。
- 第一版只允许现有 native whitelist 组件；不扩第三方插件发现和兼容层。
- 第一版统一使用一个多模态模型处理 brief 提取；不拆分“文本模型 + 图像模型”。
- `output_dir` 是 `copilot_apply_plan` 的唯一产物目录入口；实现中不开放单独 `save_path` / `preview_path`，避免 API 复杂化。
- 任何 provider-specific 高级能力都不进入 MVP：不做 Responses API 绑定、不做 Azure 专用分支、不做企业治理能力。
