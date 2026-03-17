using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.ToolState;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhConfigureComponentTool : ToolHandlerBase
{
    private readonly ComponentSessionState componentSessionState;
    private readonly ComponentConfigValidator componentConfigValidator;

    public GhConfigureComponentTool(
        IBridgeClient bridgeClient,
        ComponentSessionState componentSessionState,
        ComponentConfigValidator componentConfigValidator) : base(bridgeClient)
    {
        this.componentSessionState = componentSessionState;
        this.componentConfigValidator = componentConfigValidator;
    }

    public Task<GhConfigureComponentResponse> HandleAsync(
        string componentId,
        GhComponentConfig config,
        CancellationToken cancellationToken = default)
    {
        var normalized = componentSessionState.TryGetComponentKey(componentId, out var componentKey)
            ? componentConfigValidator.ValidateAndNormalize(componentKey!, config)
            : config;

        return BridgeClient.InvokeAsync<GhConfigureComponentResponse>(
            BridgeMethodNames.GhConfigureComponent,
            new GhConfigureComponentRequest(componentId, normalized),
            cancellationToken);
    }
}
