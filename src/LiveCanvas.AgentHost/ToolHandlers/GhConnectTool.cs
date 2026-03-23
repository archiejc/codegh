using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.ToolState;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhConnectTool : ToolHandlerBase
{
    private readonly ComponentSessionState componentSessionState;
    private readonly ConnectionValidator connectionValidator;

    public GhConnectTool(
        IBridgeClient bridgeClient,
        ComponentSessionState componentSessionState,
        ConnectionValidator connectionValidator) : base(bridgeClient)
    {
        this.componentSessionState = componentSessionState;
        this.connectionValidator = connectionValidator;
    }

    public Task<GhConnectResponse> HandleAsync(
        string sourceId,
        string sourceOutput,
        string targetId,
        string targetInput,
        CancellationToken cancellationToken = default)
    {
        if (componentSessionState.TryGetComponentKey(sourceId, out var sourceComponentKey)
            && componentSessionState.TryGetComponentKey(targetId, out var targetComponentKey)
            && connectionValidator.CanValidate(sourceComponentKey!, targetComponentKey!)
            && !connectionValidator.IsValid(sourceComponentKey!, sourceOutput, targetComponentKey!, targetInput))
        {
            throw new ArgumentException("Invalid connection request for whitelisted components/ports.");
        }

        return BridgeClient.InvokeAsync<GhConnectResponse>(
            BridgeMethodNames.GhConnect,
            new GhConnectRequest(sourceId, sourceOutput, targetId, targetInput),
            cancellationToken);
    }
}
