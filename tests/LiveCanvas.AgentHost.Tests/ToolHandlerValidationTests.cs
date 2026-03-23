using FluentAssertions;
using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.ToolHandlers;
using LiveCanvas.AgentHost.ToolState;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.AgentHost.Tests;

public class ToolHandlerValidationTests
{
    private readonly AllowedComponentRegistry allowedComponentRegistry = new();

    [Fact]
    public async Task gh_save_document_requires_explicit_absolute_gh_path()
    {
        var tool = new GhSaveDocumentTool(new RecordingBridgeClient(new GhSaveDocumentResponse(true, "/tmp/test.gh", "gh")));

        var act = async () => await tool.HandleAsync("relative.gh");
        var actBadExtension = async () => await tool.HandleAsync("/tmp/test.ghx");

        await act.Should().ThrowAsync<ArgumentException>();
        await actBadExtension.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task gh_capture_preview_requires_absolute_image_path()
    {
        var tool = new GhCapturePreviewTool(new RecordingBridgeClient(new GhCapturePreviewResponse(true, "/tmp/test.png", 1200, 800)));

        var actRelative = async () => await tool.HandleAsync("preview.png", 1200, 800);
        var actBadExtension = async () => await tool.HandleAsync("/tmp/preview.txt", 1200, 800);

        await actRelative.Should().ThrowAsync<ArgumentException>();
        await actBadExtension.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task gh_add_component_defers_unknown_component_key_to_bridge()
    {
        const string dynamicKey = "gh_guid:11111111111111111111111111111111";
        var bridgeClient = new RecordingBridgeClient(new GhAddComponentResponse("cmp_1", dynamicKey, "cmp_1", "Dynamic", 0, 0, ["A"], ["B"]));
        var tool = new GhAddComponentTool(bridgeClient, allowedComponentRegistry, new ComponentSessionState());

        await tool.HandleAsync(dynamicKey, 0, 0);

        bridgeClient.LastPayload.Should().BeOfType<GhAddComponentRequest>();
        ((GhAddComponentRequest)bridgeClient.LastPayload!).ComponentKey.Should().Be(dynamicKey);
        allowedComponentRegistry.GetRequired(dynamicKey).DisplayName.Should().Be("Dynamic");
    }

    [Fact]
    public async Task gh_new_document_clears_component_session_state()
    {
        var componentState = new ComponentSessionState();
        componentState.Track("cmp_1", V0ComponentKeys.NumberSlider);
        var tool = new GhNewDocumentTool(new RecordingBridgeClient(new GhNewDocumentResponse("doc_1", "Untitled", true)), componentState);

        await tool.HandleAsync();

        componentState.TryGetComponentKey("cmp_1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task gh_add_component_tracks_component_key_after_successful_add()
    {
        var componentState = new ComponentSessionState();
        var tool = new GhAddComponentTool(
            new RecordingBridgeClient(new GhAddComponentResponse("cmp_1", V0ComponentKeys.NumberSlider, "cmp_1", "Number Slider", 0, 0, [], ["N"])),
            allowedComponentRegistry,
            componentState);

        await tool.HandleAsync(V0ComponentKeys.NumberSlider, 0, 0);

        componentState.TryGetComponentKey("cmp_1", out var componentKey).Should().BeTrue();
        componentKey.Should().Be(V0ComponentKeys.NumberSlider);
    }

    [Fact]
    public async Task gh_configure_component_normalizes_slider_config_when_component_key_is_known()
    {
        var bridgeClient = new RecordingBridgeClient(new GhConfigureComponentResponse("cmp_1", true, new GhComponentConfig(Slider: new SliderConfig(0, 100, 100, false)), []));
        var componentState = new ComponentSessionState();
        componentState.Track("cmp_1", V0ComponentKeys.NumberSlider);
        var tool = new GhConfigureComponentTool(bridgeClient, componentState, new ComponentConfigValidator(allowedComponentRegistry));

        await tool.HandleAsync("cmp_1", new GhComponentConfig(Slider: new SliderConfig(0, 100, 150, false)));

        bridgeClient.LastPayload.Should().BeOfType<GhConfigureComponentRequest>();
        var payload = (GhConfigureComponentRequest)bridgeClient.LastPayload!;
        payload.Config.Slider!.Value.Should().Be(100);
    }

    [Fact]
    public async Task gh_connect_rejects_bad_port_names_before_bridge_call_when_component_keys_are_known()
    {
        var componentState = new ComponentSessionState();
        componentState.Track("cmp_1", V0ComponentKeys.XyPlane);
        componentState.Track("cmp_2", V0ComponentKeys.Rectangle);
        var tool = new GhConnectTool(
            new RecordingBridgeClient(new GhConnectResponse(true, "conn_1")),
            componentState,
            new ConnectionValidator(allowedComponentRegistry));

        var act = async () => await tool.HandleAsync("cmp_1", "bad", "cmp_2", "P");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task gh_configure_component_v2_normalizes_slider_adapter_when_component_key_is_known()
    {
        var bridgeClient = new RecordingBridgeClient(new GhConfigureComponentV2Response(
            "cmp_1",
            true,
            new GhComponentConfigV2(
                "gh_component_config/v2",
                [
                    new AdapterConfigComponentConfigOp(new NumberSliderAdapterConfig(0, 100, 100, false))
                ]),
            []));
        var componentState = new ComponentSessionState();
        componentState.Track("cmp_1", V0ComponentKeys.NumberSlider);
        var tool = new GhConfigureComponentV2Tool(bridgeClient, componentState, new ComponentConfigV2Validator(allowedComponentRegistry));

        await tool.HandleAsync(
            "cmp_1",
            new GhComponentConfigV2(
                "gh_component_config/v2",
                [
                    new AdapterConfigComponentConfigOp(new NumberSliderAdapterConfig(0, 100, 150, false))
                ]));

        bridgeClient.LastPayload.Should().BeOfType<GhConfigureComponentV2Request>();
        var payload = (GhConfigureComponentV2Request)bridgeClient.LastPayload!;
        var op = payload.Config.Ops.Should().ContainSingle().Subject.Should().BeOfType<AdapterConfigComponentConfigOp>().Subject;
        op.Config.Should().BeOfType<NumberSliderAdapterConfig>();
        ((NumberSliderAdapterConfig)op.Config).Value.Should().Be(100);
    }

    [Fact]
    public async Task gh_list_allowed_components_prefers_bridge_and_updates_registry()
    {
        const string dynamicKey = "gh_guid:22222222222222222222222222222222";
        var bridgeClient = new RecordingBridgeClient(new GhListAllowedComponentsResponse(
            [
                new AllowedComponentDefinition(
                    dynamicKey,
                    "Dynamic",
                    "Sets",
                    [new AllowedComponentPortInfo("A", "input", 0)],
                    [new AllowedComponentPortInfo("B", "output", 0)],
                    [],
                    [new AllowedComponentConfigOpDescriptor("set_nickname")])
            ]));
        var tool = new GhListAllowedComponentsTool(bridgeClient, allowedComponentRegistry);

        var response = await tool.HandleAsync();

        response.Components.Should().ContainSingle(component => component.ComponentKey == dynamicKey);
        allowedComponentRegistry.GetRequired(dynamicKey).DisplayName.Should().Be("Dynamic");
    }

    private sealed class RecordingBridgeClient(object response) : IBridgeClient
    {
        public string? LastMethod { get; private set; }
        public object? LastPayload { get; private set; }

        public Task<TResponse> InvokeAsync<TResponse>(string method, object? payload, CancellationToken cancellationToken = default)
        {
            LastMethod = method;
            LastPayload = payload;
            return Task.FromResult((TResponse)response);
        }
    }
}
