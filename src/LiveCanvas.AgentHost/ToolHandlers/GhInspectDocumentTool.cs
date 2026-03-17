using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Components;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhInspectDocumentTool(IBridgeClient bridgeClient) : ToolHandlerBase(bridgeClient)
{
    public Task<GhInspectDocumentResponse> HandleAsync(
        bool includeConnections = true,
        bool includeRuntimeMessages = true,
        CancellationToken cancellationToken = default) =>
        BridgeClient.InvokeAsync<GhInspectDocumentResponse>(
            BridgeMethodNames.GhInspectDocument,
            new GhInspectDocumentRequest(includeConnections, includeRuntimeMessages),
            cancellationToken);
}
