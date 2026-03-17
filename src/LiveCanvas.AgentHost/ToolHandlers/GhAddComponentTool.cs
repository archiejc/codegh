using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.ToolState;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Core.AllowedComponents;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhAddComponentTool : ToolHandlerBase
{
    private readonly AllowedComponentRegistry allowedComponentRegistry;
    private readonly ComponentSessionState componentSessionState;

    public GhAddComponentTool(
        IBridgeClient bridgeClient,
        AllowedComponentRegistry allowedComponentRegistry,
        ComponentSessionState componentSessionState) : base(bridgeClient)
    {
        this.allowedComponentRegistry = allowedComponentRegistry;
        this.componentSessionState = componentSessionState;
    }

    public async Task<GhAddComponentResponse> HandleAsync(
        string componentKey,
        double x,
        double y,
        CancellationToken cancellationToken = default)
    {
        _ = allowedComponentRegistry.GetRequired(componentKey);

        var response = await BridgeClient.InvokeAsync<GhAddComponentResponse>(
            BridgeMethodNames.GhAddComponent,
            new GhAddComponentRequest(componentKey, x, y),
            cancellationToken);

        componentSessionState.Track(response.ComponentId, response.ComponentKey);
        return response;
    }
}
