namespace LiveCanvas.AgentHost.BridgeClient;

public interface IBridgeClient
{
    Task<TResponse> InvokeAsync<TResponse>(string method, object? payload, CancellationToken cancellationToken = default);
}
