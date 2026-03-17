using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Session;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhSessionInfoTool(IBridgeClient bridgeClient) : ToolHandlerBase(bridgeClient)
{
    public Task<GhSessionInfoResponse> HandleAsync(CancellationToken cancellationToken = default) =>
        BridgeClient.InvokeAsync<GhSessionInfoResponse>(BridgeMethodNames.GhSessionInfo, new GhSessionInfoRequest(), cancellationToken);
}
