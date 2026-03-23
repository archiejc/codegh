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
        var response = await BridgeClient.InvokeAsync<GhAddComponentResponse>(
            BridgeMethodNames.GhAddComponent,
            new GhAddComponentRequest(componentKey, x, y),
            cancellationToken);

        if (!allowedComponentRegistry.TryGet(response.ComponentKey, out _))
        {
            allowedComponentRegistry.Upsert(new AllowedComponentDefinition(
                ComponentKey: response.ComponentKey,
                DisplayName: response.DisplayName,
                Category: "Discovered",
                Inputs: response.Inputs.Select((name, index) => new AllowedComponentPortInfo(name, "input", index)).ToArray(),
                Outputs: response.Outputs.Select((name, index) => new AllowedComponentPortInfo(name, "output", index)).ToArray(),
                ConfigFields: [new AllowedComponentConfigField("nickname", "string", false)]));
        }

        componentSessionState.Track(response.ComponentId, response.ComponentKey);
        return response;
    }
}
