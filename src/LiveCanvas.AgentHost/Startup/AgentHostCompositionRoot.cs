using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.Copilot;
using LiveCanvas.AgentHost.Mcp;
using LiveCanvas.AgentHost.ToolHandlers;
using LiveCanvas.AgentHost.ToolState;
using LiveCanvas.Contracts.Copilot;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.AgentHost.Startup;

internal sealed class AgentHostCompositionRoot
{
    public StdioMcpServer CreateServer() => new(CreateExecutor());

    internal McpToolExecutor CreateExecutor()
    {
        var registry = new AllowedComponentRegistry();
        var componentSessionState = new ComponentSessionState();
        var bridgeClient = new WebSocketBridgeClient();

        return new McpToolExecutor(
            new GhSessionInfoTool(bridgeClient),
            new GhNewDocumentTool(bridgeClient, componentSessionState),
            new GhListAllowedComponentsTool(registry),
            new GhAddComponentTool(bridgeClient, registry, componentSessionState),
            new GhConfigureComponentTool(bridgeClient, componentSessionState, new ComponentConfigValidator(registry)),
            new GhConnectTool(bridgeClient, componentSessionState, new ConnectionValidator(registry)),
            new GhDeleteComponentTool(bridgeClient, componentSessionState),
            new GhSolveTool(bridgeClient),
            new GhInspectDocumentTool(bridgeClient),
            new GhCapturePreviewTool(bridgeClient),
            new GhSaveDocumentTool(bridgeClient),
            new NotImplementedCopilotPlanService(),
            new NotImplementedCopilotApplyService());
    }

    private sealed class NotImplementedCopilotPlanService : ICopilotPlanService
    {
        public Task<CopilotPlanResponse> CreatePlanAsync(CopilotPlanRequest request, CancellationToken cancellationToken = default) =>
            throw new McpToolUnavailableException(
                ToolDefinitions.CopilotPlan,
                "Tool 'copilot_plan' is unavailable in this build.");
    }

    private sealed class NotImplementedCopilotApplyService : ICopilotApplyService
    {
        public Task<CopilotApplyPlanResponse> ApplyPlanAsync(CopilotApplyPlanRequest request, CancellationToken cancellationToken = default) =>
            throw new McpToolUnavailableException(
                ToolDefinitions.CopilotApplyPlan,
                "Tool 'copilot_apply_plan' is unavailable in this build.");
    }
}
