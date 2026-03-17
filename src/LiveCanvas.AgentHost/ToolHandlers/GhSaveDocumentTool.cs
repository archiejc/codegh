using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Documents;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhSaveDocumentTool(IBridgeClient bridgeClient) : ToolHandlerBase(bridgeClient)
{
    public Task<GhSaveDocumentResponse> HandleAsync(string path, CancellationToken cancellationToken = default)
    {
        ToolPathValidator.RequireAbsoluteGhPath(path, nameof(path));

        return BridgeClient.InvokeAsync<GhSaveDocumentResponse>(BridgeMethodNames.GhSaveDocument, new GhSaveDocumentRequest(path), cancellationToken);
    }
}
