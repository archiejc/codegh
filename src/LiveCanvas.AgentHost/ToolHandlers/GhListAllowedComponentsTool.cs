using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Core.AllowedComponents;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhListAllowedComponentsTool
{
    private readonly IBridgeClient bridgeClient;
    private readonly AllowedComponentRegistry allowedComponentRegistry;

    public GhListAllowedComponentsTool(IBridgeClient bridgeClient, AllowedComponentRegistry allowedComponentRegistry)
    {
        this.bridgeClient = bridgeClient;
        this.allowedComponentRegistry = allowedComponentRegistry;
    }

    public async Task<GhListAllowedComponentsResponse> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await bridgeClient.InvokeAsync<GhListAllowedComponentsResponse>(
                BridgeMethodNames.GhListAllowedComponents,
                new GhListAllowedComponentsRequest(),
                cancellationToken);
            allowedComponentRegistry.UpsertRange(response.Components);
            return response;
        }
        catch (BridgeClientUnavailableException)
        {
            return new GhListAllowedComponentsResponse(allowedComponentRegistry.All());
        }
    }
}
