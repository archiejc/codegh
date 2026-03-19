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

public class CopilotApplyToolTests
{
    private readonly AllowedComponentRegistry allowedComponentRegistry = new();

    [Fact]
    public async Task copilot_apply_plan_routes_through_injected_apply_service()
    {
        var bridgeClient = new RejectingBridgeClient();
        var executionPlan = new CopilotExecutionPlan(
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

        var expectedResponse = new CopilotApplyPlanResponse(
            "succeeded",
            0,
            [],
            new GhNewDocumentResponse("doc_1", "Tower", true),
            new GhSolveResponse(true, "ok", 1, 0, 0, []),
            new GhInspectDocumentResponse("doc_1", [], [], [], null, new GhPreviewSummary(true, 1)),
            "/tmp/out/preview.png",
            "/tmp/out/document.gh",
            []);

        var applyService = new RecordingCopilotApplyService(expectedResponse);
        var executor = CreateExecutor(bridgeClient, new StubCopilotPlanService(), applyService);
        var arguments = JsonSerializer.SerializeToElement(new CopilotApplyPlanRequest(executionPlan, "/tmp/out", 1200, 800, false));

        var result = await executor.InvokeAsync("copilot_apply_plan", arguments, CancellationToken.None);

        result.Should().BeEquivalentTo(expectedResponse);
        applyService.LastRequest.Should().NotBeNull();
        applyService.LastRequest!.ExecutionPlan.Should().BeEquivalentTo(executionPlan);
        applyService.LastRequest.OutputDir.Should().Be("/tmp/out");
        applyService.LastRequest.PreviewWidth.Should().Be(1200);
        applyService.LastRequest.PreviewHeight.Should().Be(800);
        applyService.LastRequest.ExpireAll.Should().BeFalse();
    }

    [Fact]
    public async Task copilot_apply_plan_requires_execution_plan_before_service_invocation()
    {
        var bridgeClient = new RejectingBridgeClient();
        var applyService = new RecordingCopilotApplyService(new CopilotApplyPlanResponse("succeeded", 0, [], null, null, null, null, null, []));
        var executor = CreateExecutor(bridgeClient, new StubCopilotPlanService(), applyService);
        var arguments = JsonDocument.Parse("""{"output_dir":"/tmp/out"}""").RootElement.Clone();

        var act = async () => await executor.InvokeAsync("copilot_apply_plan", arguments, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*execution_plan*");
        applyService.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task copilot_apply_plan_rejects_invalid_execution_plan_before_service_invocation()
    {
        var bridgeClient = new RejectingBridgeClient();
        var applyService = new RecordingCopilotApplyService(new CopilotApplyPlanResponse("succeeded", 0, [], null, null, null, null, null, []));
        var executor = CreateExecutor(bridgeClient, new StubCopilotPlanService(), applyService);
        var arguments = JsonDocument.Parse("""{"execution_plan":"not-an-object"}""").RootElement.Clone();

        var act = async () => await executor.InvokeAsync("copilot_apply_plan", arguments, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*execution_plan*");
        applyService.LastRequest.Should().BeNull();
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

    private sealed class RecordingCopilotApplyService(CopilotApplyPlanResponse response) : ICopilotApplyService
    {
        public CopilotApplyPlanRequest? LastRequest { get; private set; }

        public Task<CopilotApplyPlanResponse> ApplyPlanAsync(CopilotApplyPlanRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    private sealed class StubCopilotPlanService : ICopilotPlanService
    {
        public Task<CopilotPlanResponse> CreatePlanAsync(CopilotPlanRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class RejectingBridgeClient : IBridgeClient
    {
        public Task<TResponse> InvokeAsync<TResponse>(string method, object? payload, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"Bridge should not be called during copilot_apply_plan routing test. Method={method}");
    }
}
