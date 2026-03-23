using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.ToolState;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhConfigureComponentV2Tool : ToolHandlerBase
{
    private readonly ComponentSessionState componentSessionState;
    private readonly ComponentConfigV2Validator componentConfigValidator;

    public GhConfigureComponentV2Tool(
        IBridgeClient bridgeClient,
        ComponentSessionState componentSessionState,
        ComponentConfigV2Validator componentConfigValidator) : base(bridgeClient)
    {
        this.componentSessionState = componentSessionState;
        this.componentConfigValidator = componentConfigValidator;
    }

    public Task<GhConfigureComponentV2Response> HandleAsync(
        string componentId,
        GhComponentConfigV2 config,
        CancellationToken cancellationToken = default)
    {
        var componentKey = componentSessionState.TryGetComponentKey(componentId, out var trackedKey)
            ? trackedKey
            : null;
        var normalized = componentConfigValidator.ValidateAndNormalize(componentKey, config);

        return BridgeClient.InvokeAsync<GhConfigureComponentV2Response>(
            BridgeMethodNames.GhConfigureComponentV2,
            new GhConfigureComponentV2Request(componentId, normalized),
            cancellationToken);
    }
}
