using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Documents;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhCapturePreviewTool(IBridgeClient bridgeClient) : ToolHandlerBase(bridgeClient)
{
    public Task<GhCapturePreviewResponse> HandleAsync(
        string path,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ToolPathValidator.RequireAbsolutePreviewPath(path, nameof(path));

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Preview width and height must be positive.");
        }

        return BridgeClient.InvokeAsync<GhCapturePreviewResponse>(
            BridgeMethodNames.GhCapturePreview,
            new GhCapturePreviewRequest(path, "rhino_viewport", width, height),
            cancellationToken);
    }
}
