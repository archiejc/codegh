using System.Text.Json;
using FluentAssertions;
using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.Copilot;
using LiveCanvas.AgentHost.Mcp;
using LiveCanvas.AgentHost.ToolHandlers;
using LiveCanvas.AgentHost.ToolState;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Copilot;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Contracts.Planner;
using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Planner;
using LiveCanvas.Core.ReferenceInterpretation;
using LiveCanvas.Core.Repair;
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
            new GhListAllowedComponentsTool(bridgeClient, allowedComponentRegistry),
            new GhAddComponentTool(bridgeClient, allowedComponentRegistry, componentState),
            new GhConfigureComponentTool(bridgeClient, componentState, new ComponentConfigValidator(allowedComponentRegistry)),
            new GhConfigureComponentV2Tool(bridgeClient, componentState, new ComponentConfigV2Validator(allowedComponentRegistry)),
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

public class CopilotApplyServiceTests
{
    private readonly AllowedComponentRegistry allowedComponentRegistry = new();

    [Fact]
    public async Task apply_service_rejects_schema_version_mismatch_before_bridge_call()
    {
        var bridgeClient = new ScriptedBridgeClient();
        var service = CreateService(bridgeClient);
        var request = new CopilotApplyPlanRequest(CreateExecutionPlan() with { SchemaVersion = "copilot_execution_plan/v0" });

        var act = async () => await service.ApplyPlanAsync(request);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*schema_version*");
        bridgeClient.Methods.Should().BeEmpty();
    }

    [Fact]
    public async Task apply_service_executes_bridge_chain_and_writes_artifacts()
    {
        var bridgeClient = new ScriptedBridgeClient();
        var service = CreateService(bridgeClient);
        var outputDir = Path.Combine(Path.GetTempPath(), "livecanvas-apply-tests", Guid.NewGuid().ToString("N"));

        var response = await service.ApplyPlanAsync(new CopilotApplyPlanRequest(CreateExecutionPlan(), outputDir, 1200, 800, false));

        response.Status.Should().Be("succeeded");
        response.PreviewPath.Should().Be(Path.Combine(outputDir, "preview.png"));
        response.DocumentPath.Should().Be(Path.Combine(outputDir, "document.gh"));
        File.Exists(response.PreviewPath!).Should().BeTrue();
        File.Exists(response.DocumentPath!).Should().BeTrue();
        bridgeClient.Methods[0].Should().Be(BridgeMethodNames.GhNewDocument);
        bridgeClient.Methods.Should().Contain(BridgeMethodNames.GhSolve);
        bridgeClient.Methods.Should().Contain(BridgeMethodNames.GhInspectDocument);
        bridgeClient.Methods.Should().Contain(BridgeMethodNames.GhCapturePreview);
        bridgeClient.Methods.Should().Contain(BridgeMethodNames.GhSaveDocument);
        bridgeClient.Methods.Count(method => method == BridgeMethodNames.GhAddComponent).Should().Be(CreateExecutionPlan().GraphPlan.Components.Count);
        bridgeClient.Methods.Count(method => method == BridgeMethodNames.GhConfigureComponent).Should().Be(CreateExecutionPlan().GraphPlan.Components.Count);
        bridgeClient.Methods.Count(method => method == BridgeMethodNames.GhConnect).Should().Be(CreateExecutionPlan().GraphPlan.Connections.Count);
    }

    [Fact]
    public async Task apply_service_retries_loft_failure_with_stacked_bars()
    {
        var bridgeClient = new ScriptedBridgeClient
        {
            InspectGeometrySequence = new Queue<bool>([false, true])
        };
        var service = CreateService(bridgeClient);
        var outputDir = Path.Combine(Path.GetTempPath(), "livecanvas-apply-tests", Guid.NewGuid().ToString("N"));

        var response = await service.ApplyPlanAsync(new CopilotApplyPlanRequest(CreateExecutionPlan(MassingTemplate.LoftedTaper), outputDir));

        response.Status.Should().Be("succeeded");
        response.RepairIterations.Should().Be(1);
        response.RepairActions.Should().Contain("downgrade_loft_to_stacked_bars");
    }

    [Fact]
    public async Task apply_service_returns_null_paths_when_capture_repeatedly_fails()
    {
        var bridgeClient = new ScriptedBridgeClient { FailCapture = true };
        var service = CreateService(bridgeClient);
        var outputDir = Path.Combine(Path.GetTempPath(), "livecanvas-apply-tests", Guid.NewGuid().ToString("N"));

        var response = await service.ApplyPlanAsync(new CopilotApplyPlanRequest(CreateExecutionPlan(), outputDir));

        response.Status.Should().Be("repair_exhausted");
        response.PreviewPath.Should().BeNull();
        response.DocumentPath.Should().BeNull();
    }

    private CopilotApplyService CreateService(IBridgeClient bridgeClient)
    {
        var componentState = new ComponentSessionState();
        var componentConfigValidator = new ComponentConfigValidator(allowedComponentRegistry);
        var connectionValidator = new ConnectionValidator(allowedComponentRegistry);
        var newDocumentTool = new GhNewDocumentTool(bridgeClient, componentState);
        var addComponentTool = new GhAddComponentTool(bridgeClient, allowedComponentRegistry, componentState);
        var configureComponentTool = new GhConfigureComponentTool(bridgeClient, componentState, componentConfigValidator);
        var connectTool = new GhConnectTool(bridgeClient, componentState, connectionValidator);
        var solveTool = new GhSolveTool(bridgeClient);
        var inspectTool = new GhInspectDocumentTool(bridgeClient);
        var captureTool = new GhCapturePreviewTool(bridgeClient);
        var saveTool = new GhSaveDocumentTool(bridgeClient);

        return new CopilotApplyService(
            new CopilotExecutionPlanValidator(allowedComponentRegistry, componentConfigValidator, connectionValidator),
            new BridgeGraphExecutor(newDocumentTool, addComponentTool, configureComponentTool, connectTool, solveTool, inspectTool, captureTool, saveTool),
            new TemplatePlanner(),
            new TemplateGraphParameterizer(),
            new RepairEngine(),
            new CopilotFailureClassifier());
    }

    private static CopilotExecutionPlan CreateExecutionPlan(string templateName = MassingTemplate.SingleExtrusion)
    {
        var brief = new ReferenceBrief(
            BuildingType: "tower",
            SiteContext: "urban",
            MassingStrategy: templateName,
            ApproxDimensions: new ApproxDimensions(30, 20, 90),
            Leveling: new LevelingHints(18, 72, 3),
            TransformHints: new TransformHints(0, 0.7, null),
            StyleHints: new StyleHints([200, 200, 210], "clean"),
            Confidence: 0.8,
            Assumptions: ["test"]);
        var planner = new TemplatePlanner();
        var parameterizer = new TemplateGraphParameterizer();
        var graphPlan = parameterizer.Parameterize(planner.CreatePlan(brief), brief);

        return new CopilotExecutionPlan(
            InputPrompt: "make a tower",
            InputImages: [],
            ReferenceBrief: brief,
            TemplateName: graphPlan.TemplateName,
            GraphPlan: graphPlan,
            Assumptions: brief.Assumptions,
            Warnings: [],
            SuggestedDocumentName: "make-a-tower");
    }

    private sealed class ScriptedBridgeClient : IBridgeClient
    {
        private int nextComponentId = 1;
        public bool FailCapture { get; init; }
        public Queue<bool>? InspectGeometrySequence { get; init; }
        public List<string> Methods { get; } = [];

        public Task<TResponse> InvokeAsync<TResponse>(string method, object? payload, CancellationToken cancellationToken = default)
        {
            Methods.Add(method);

            object response = method switch
            {
                BridgeMethodNames.GhNewDocument => new GhNewDocumentResponse("doc_1", "Tower", true),
                BridgeMethodNames.GhAddComponent => BuildAddComponentResponse((GhAddComponentRequest)payload!),
                BridgeMethodNames.GhConfigureComponent => new GhConfigureComponentResponse(((GhConfigureComponentRequest)payload!).ComponentId, true, ((GhConfigureComponentRequest)payload!).Config, []),
                BridgeMethodNames.GhConnect => new GhConnectResponse(true, Guid.NewGuid().ToString("N")),
                BridgeMethodNames.GhSolve => new GhSolveResponse(true, "ok", 1, 0, 0, []),
                BridgeMethodNames.GhInspectDocument => BuildInspectResponse(),
                BridgeMethodNames.GhCapturePreview => BuildCaptureResponse((GhCapturePreviewRequest)payload!),
                BridgeMethodNames.GhSaveDocument => BuildSaveResponse((GhSaveDocumentRequest)payload!),
                _ => throw new InvalidOperationException($"Unexpected bridge method {method}")
            };

            return Task.FromResult((TResponse)response);
        }

        private GhAddComponentResponse BuildAddComponentResponse(GhAddComponentRequest request)
        {
            var componentId = $"cmp_{nextComponentId++}";
            return new GhAddComponentResponse(componentId, request.ComponentKey, componentId, request.ComponentKey, request.X, request.Y, [], []);
        }

        private GhInspectDocumentResponse BuildInspectResponse()
        {
            var hasGeometry = InspectGeometrySequence is { Count: > 0 }
                ? InspectGeometrySequence.Dequeue()
                : true;

            return new GhInspectDocumentResponse(
                "doc_1",
                [],
                [],
                [],
                hasGeometry ? new GhBounds(0, 0, 0, 10, 10, 10) : null,
                new GhPreviewSummary(hasGeometry, hasGeometry ? 1 : 0));
        }

        private GhCapturePreviewResponse BuildCaptureResponse(GhCapturePreviewRequest request)
        {
            if (FailCapture)
            {
                throw new InvalidOperationException("capture failed");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(request.Path)!);
            File.WriteAllBytes(request.Path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j5wsAAAAASUVORK5CYII="));
            return new GhCapturePreviewResponse(true, request.Path, request.Width, request.Height);
        }

        private GhSaveDocumentResponse BuildSaveResponse(GhSaveDocumentRequest request)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.Path)!);
            File.WriteAllText(request.Path, "mock gh document");
            return new GhSaveDocumentResponse(true, request.Path, "gh");
        }
    }
}
