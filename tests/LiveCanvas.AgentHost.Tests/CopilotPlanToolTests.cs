using System.Text.Json;
using FluentAssertions;
using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.Copilot;
using LiveCanvas.AgentHost.Mcp;
using LiveCanvas.AgentHost.ToolHandlers;
using LiveCanvas.AgentHost.ToolState;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Copilot;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Contracts.Planner;
using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.AgentHost.Tests;

public class CopilotPlanToolTests
{
    private readonly AllowedComponentRegistry allowedComponentRegistry = new();

    [Fact]
    public async Task copilot_plan_routes_through_injected_plan_service_without_bridge_calls()
    {
        var bridgeClient = new RejectingBridgeClient();
        var expectedPlan = new CopilotExecutionPlan(
            InputPrompt: "make a tower",
            InputImages: [],
            ReferenceBrief: new ReferenceBrief(
                "tower",
                "urban",
                "single_extrusion",
                new ApproxDimensions(30, 20, 90),
                new LevelingHints(null, null, null),
                new TransformHints(null, null, null),
                new StyleHints(null, null),
                0.8,
                ["assumed metric units"]),
            TemplateName: "single_extrusion",
            GraphPlan: new TemplateGraphPlan(
                "single_extrusion",
                [new GraphComponentPlan("width", "number_slider", 0, 0, new GhComponentConfig(Slider: new SliderConfig(5, 200, 30, false)))],
                []),
            Assumptions: ["assumed metric units"],
            Warnings: [],
            SuggestedDocumentName: "make-a-tower");
        var expectedResponse = new CopilotPlanResponse(expectedPlan);

        var planService = new RecordingCopilotPlanService(expectedResponse);
        var executor = CreateExecutor(bridgeClient, planService, new StubCopilotApplyService());
        var arguments = JsonDocument.Parse("""{"prompt":"make a tower","image_paths":["/tmp/reference.png"]}""").RootElement.Clone();

        var result = await executor.InvokeAsync("copilot_plan", arguments, CancellationToken.None);

        result.Should().BeEquivalentTo(expectedResponse);
        planService.LastRequest.Should().NotBeNull();
        planService.LastRequest!.Prompt.Should().Be("make a tower");
        planService.LastRequest.ImagePaths.Should().Equal("/tmp/reference.png");
    }

    [Fact]
    public async Task copilot_plan_requires_prompt_before_service_invocation()
    {
        var bridgeClient = new RejectingBridgeClient();
        var planService = new RecordingCopilotPlanService(new CopilotPlanResponse(CreateExecutionPlan()));
        var executor = CreateExecutor(bridgeClient, planService, new StubCopilotApplyService());
        var arguments = JsonDocument.Parse("""{"image_paths":["/tmp/reference.png"]}""").RootElement.Clone();

        var act = async () => await executor.InvokeAsync("copilot_plan", arguments, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*prompt*");
        planService.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task copilot_plan_rejects_non_string_prompt_before_service_invocation()
    {
        var bridgeClient = new RejectingBridgeClient();
        var planService = new RecordingCopilotPlanService(new CopilotPlanResponse(CreateExecutionPlan()));
        var executor = CreateExecutor(bridgeClient, planService, new StubCopilotApplyService());
        var arguments = JsonDocument.Parse("""{"prompt":123}""").RootElement.Clone();

        var act = async () => await executor.InvokeAsync("copilot_plan", arguments, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*prompt*");
        planService.LastRequest.Should().BeNull();
    }

    private McpToolExecutor CreateExecutor(
        IBridgeClient bridgeClient,
        ICopilotPlanService planService,
        ICopilotApplyService applyService)
    {
        var componentState = new ComponentSessionState();

        return new McpToolExecutor(
            new GhSessionInfoTool(bridgeClient),
            new GhNewDocumentTool(bridgeClient, componentState),
            new GhListAllowedComponentsTool(allowedComponentRegistry),
            new GhAddComponentTool(bridgeClient, allowedComponentRegistry, componentState),
            new GhConfigureComponentTool(bridgeClient, componentState, new ComponentConfigValidator(allowedComponentRegistry)),
            new GhConnectTool(bridgeClient, componentState, new ConnectionValidator(allowedComponentRegistry)),
            new GhDeleteComponentTool(bridgeClient, componentState),
            new GhSolveTool(bridgeClient),
            new GhInspectDocumentTool(bridgeClient),
            new GhCapturePreviewTool(bridgeClient),
            new GhSaveDocumentTool(bridgeClient),
            planService,
            applyService);
    }

    private static CopilotExecutionPlan CreateExecutionPlan() =>
        new(
            InputPrompt: "make a tower",
            InputImages: [],
            ReferenceBrief: new ReferenceBrief(
                "tower",
                "urban",
                "single_extrusion",
                new ApproxDimensions(30, 20, 90),
                new LevelingHints(null, null, null),
                new TransformHints(null, null, null),
                new StyleHints(null, null),
                0.8,
                ["assumed metric units"]),
            TemplateName: "single_extrusion",
            GraphPlan: new TemplateGraphPlan(
                "single_extrusion",
                [new GraphComponentPlan("width", "number_slider", 0, 0, new GhComponentConfig(Slider: new SliderConfig(5, 200, 30, false)))],
                []),
            Assumptions: ["assumed metric units"],
            Warnings: [],
            SuggestedDocumentName: "make-a-tower");

    private sealed class RecordingCopilotPlanService(CopilotPlanResponse result) : ICopilotPlanService
    {
        public CopilotPlanRequest? LastRequest { get; private set; }

        public Task<CopilotPlanResponse> CreatePlanAsync(CopilotPlanRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class StubCopilotApplyService : ICopilotApplyService
    {
        public Task<CopilotApplyPlanResponse> ApplyPlanAsync(CopilotApplyPlanRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class RejectingBridgeClient : IBridgeClient
    {
        public Task<TResponse> InvokeAsync<TResponse>(string method, object? payload, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"Bridge should not be called during copilot_plan routing. Method={method}");
    }
}
