namespace LiveCanvas.AgentHost.BridgeClient;

public sealed class BridgeClientUnavailableException : Exception
{
    public BridgeClientUnavailableException(string message) : base(message)
    {
    }
}
