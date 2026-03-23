using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.Copilot;
using LiveCanvas.AgentHost.Mcp;
using LiveCanvas.AgentHost.ToolHandlers;
using LiveCanvas.AgentHost.ToolState;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Planner;
using LiveCanvas.Core.ReferenceInterpretation;
using LiveCanvas.Core.Repair;
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
        var componentConfigValidator = new ComponentConfigValidator(registry);
        var componentConfigV2Validator = new ComponentConfigV2Validator(registry);
        var connectionValidator = new ConnectionValidator(registry);

        var sessionInfoTool = new GhSessionInfoTool(bridgeClient);
        var newDocumentTool = new GhNewDocumentTool(bridgeClient, componentSessionState);
        var listAllowedComponentsTool = new GhListAllowedComponentsTool(bridgeClient, registry);
        var addComponentTool = new GhAddComponentTool(bridgeClient, registry, componentSessionState);
        var configureComponentTool = new GhConfigureComponentTool(bridgeClient, componentSessionState, componentConfigValidator);
        var configureComponentV2Tool = new GhConfigureComponentV2Tool(bridgeClient, componentSessionState, componentConfigV2Validator);
        var connectTool = new GhConnectTool(bridgeClient, componentSessionState, connectionValidator);
        var deleteComponentTool = new GhDeleteComponentTool(bridgeClient, componentSessionState);
        var solveTool = new GhSolveTool(bridgeClient);
        var inspectDocumentTool = new GhInspectDocumentTool(bridgeClient);
        var capturePreviewTool = new GhCapturePreviewTool(bridgeClient);
        var saveDocumentTool = new GhSaveDocumentTool(bridgeClient);

        var copilotOptions = CopilotOptions.FromEnvironment();
        var planner = new TemplatePlanner();
        var parameterizer = new TemplateGraphParameterizer();
        var executionPlanValidator = new CopilotExecutionPlanValidator(registry, componentConfigValidator, connectionValidator);
        var bridgeGraphExecutor = new BridgeGraphExecutor(
            newDocumentTool,
            addComponentTool,
            configureComponentTool,
            connectTool,
            solveTool,
            inspectDocumentTool,
            capturePreviewTool,
            saveDocumentTool);
        var httpClient = new HttpClient();
        var modelClient = new OpenAiCompatibleCopilotModelClient(httpClient, copilotOptions);
        var copilotPlanService = new CopilotPlanService(
            copilotOptions,
            modelClient,
            new ReferenceBriefSimplifier(),
            planner,
            parameterizer);
        var copilotApplyService = new CopilotApplyService(
            executionPlanValidator,
            bridgeGraphExecutor,
            planner,
            parameterizer,
            new RepairEngine(),
            new CopilotFailureClassifier());

        return new McpToolExecutor(
            sessionInfoTool,
            newDocumentTool,
            listAllowedComponentsTool,
            addComponentTool,
            configureComponentTool,
            configureComponentV2Tool,
            connectTool,
            deleteComponentTool,
            solveTool,
            inspectDocumentTool,
            capturePreviewTool,
            saveDocumentTool,
            copilotPlanService,
            copilotApplyService);
    }
}
