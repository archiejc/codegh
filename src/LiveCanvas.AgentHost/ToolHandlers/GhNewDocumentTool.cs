using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.ToolState;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Documents;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhNewDocumentTool : ToolHandlerBase
{
    private readonly ComponentSessionState componentSessionState;

    public GhNewDocumentTool(IBridgeClient bridgeClient, ComponentSessionState componentSessionState) : base(bridgeClient)
    {
        this.componentSessionState = componentSessionState;
    }

    public async Task<GhNewDocumentResponse> HandleAsync(string? name = null, CancellationToken cancellationToken = default)
    {
        var response = await BridgeClient.InvokeAsync<GhNewDocumentResponse>(
            BridgeMethodNames.GhNewDocument,
            new GhNewDocumentRequest(name),
            cancellationToken);

        componentSessionState.Clear();
        return response;
    }
}
