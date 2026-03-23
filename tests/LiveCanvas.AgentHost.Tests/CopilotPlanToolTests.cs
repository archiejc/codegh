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
using LiveCanvas.Core.Planner;
using LiveCanvas.Core.ReferenceInterpretation;
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

public class CopilotPlanServiceTests
{
    [Fact]
    public async Task copilot_plan_service_rejects_too_many_images_before_provider_call()
    {
        var modelClient = new RecordingModelClient("""{"buildingType":"tower","siteContext":"urban","massingStrategy":"single_extrusion","approxDimensions":{"width":30,"depth":20,"height":90},"leveling":{"podiumHeight":null,"towerHeight":null,"stepCount":3},"transformHints":{"rotationDegrees":null,"taperRatio":1.0,"offsetPattern":null},"styleHints":{"color":[255,255,255],"silhouette":"clean"},"confidence":0.8,"assumptions":["metric"]}""");
        var service = CreateService(modelClient);

        var act = async () => await service.CreatePlanAsync(
            new CopilotPlanRequest(
                "tower",
                Enumerable.Range(1, 5).Select(index => Path.Combine(Path.GetTempPath(), $"image-{index}.png")).ToArray()));

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*up to 4 images*");
        modelClient.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task copilot_plan_service_rejects_relative_image_path_before_provider_call()
    {
        var modelClient = new RecordingModelClient("""{"buildingType":"tower","siteContext":"urban","massingStrategy":"single_extrusion","approxDimensions":{"width":30,"depth":20,"height":90},"leveling":{"podiumHeight":null,"towerHeight":null,"stepCount":3},"transformHints":{"rotationDegrees":null,"taperRatio":1.0,"offsetPattern":null},"styleHints":{"color":[255,255,255],"silhouette":"clean"},"confidence":0.8,"assumptions":["metric"]}""");
        var service = CreateService(modelClient);

        var act = async () => await service.CreatePlanAsync(new CopilotPlanRequest("tower", ["relative.png"]));

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*absolute paths only*");
        modelClient.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task copilot_plan_service_rejects_invalid_model_json()
    {
        var modelClient = new RecordingModelClient("{not json}");
        var service = CreateService(modelClient);
        var imagePath = await CreateTempImageAsync();

        var act = async () => await service.CreatePlanAsync(new CopilotPlanRequest("tower", [imagePath]));

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*invalid JSON*");
        modelClient.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task copilot_plan_service_returns_parameterized_execution_plan_and_clamp_warning()
    {
        var modelClient = new RecordingModelClient(
            """
            {"buildingType":"tower","siteContext":"urban","massingStrategy":"stepped_extrusions","approxDimensions":{"width":500,"depth":1,"height":1000},"leveling":{"podiumHeight":null,"towerHeight":null,"stepCount":99},"transformHints":{"rotationDegrees":20,"taperRatio":0.6,"offsetPattern":"staggered"},"styleHints":{"color":[12,34,56],"silhouette":"angular"},"confidence":0.9,"assumptions":["metric units"]}
            """);
        var service = CreateService(modelClient);
        var imagePath = await CreateTempImageAsync();

        var response = await service.CreatePlanAsync(new CopilotPlanRequest("Make a stepped tower", [imagePath]));

        response.ExecutionPlan.InputImages.Should().Equal(imagePath);
        response.ExecutionPlan.Warnings.Should().Contain("dimensions_clamped");
        response.ExecutionPlan.GraphPlan.Components.Any(component => component.Config.Slider is not null).Should().BeTrue();
        response.ExecutionPlan.SuggestedDocumentName.Should().Be("make-a-stepped-tower");
    }

    private static CopilotPlanService CreateService(ICopilotModelClient modelClient) =>
        new(
            new CopilotOptions("https://example.com", "test-key", "test-model"),
            modelClient,
            new ReferenceBriefSimplifier(),
            new TemplatePlanner(),
            new TemplateGraphParameterizer());

    private static async Task<string> CreateTempImageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "livecanvas-plan-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "reference.png");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j5wsAAAAASUVORK5CYII="));
        return path;
    }

    private sealed class RecordingModelClient(string responseJson) : ICopilotModelClient
    {
        public int CallCount { get; private set; }

        public Task<string> CreateReferenceBriefJsonAsync(string prompt, IReadOnlyList<string> imageDataUrls, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(responseJson);
        }
    }
}
