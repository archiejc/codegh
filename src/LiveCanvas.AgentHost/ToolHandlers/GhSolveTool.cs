using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Components;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhSolveTool(IBridgeClient bridgeClient) : ToolHandlerBase(bridgeClient)
{
    public Task<GhSolveResponse> HandleAsync(bool expireAll = true, CancellationToken cancellationToken = default) =>
        BridgeClient.InvokeAsync<GhSolveResponse>(BridgeMethodNames.GhSolve, new GhSolveRequest(expireAll), cancellationToken);
}
