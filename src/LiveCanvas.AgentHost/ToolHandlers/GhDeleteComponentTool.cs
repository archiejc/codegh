using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.ToolState;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Components;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhDeleteComponentTool : ToolHandlerBase
{
    private readonly ComponentSessionState componentSessionState;

    public GhDeleteComponentTool(IBridgeClient bridgeClient, ComponentSessionState componentSessionState) : base(bridgeClient)
    {
        this.componentSessionState = componentSessionState;
    }

    public async Task<GhDeleteComponentResponse> HandleAsync(string componentId, CancellationToken cancellationToken = default)
    {
        var response = await BridgeClient.InvokeAsync<GhDeleteComponentResponse>(
            BridgeMethodNames.GhDeleteComponent,
            new GhDeleteComponentRequest(componentId),
            cancellationToken);

        if (response.Deleted)
        {
            componentSessionState.Remove(componentId);
        }

        return response;
    }
}
