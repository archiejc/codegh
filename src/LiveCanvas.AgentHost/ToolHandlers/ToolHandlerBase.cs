using LiveCanvas.AgentHost.BridgeClient;

namespace LiveCanvas.AgentHost.ToolHandlers;

public abstract class ToolHandlerBase
{
    protected ToolHandlerBase(IBridgeClient bridgeClient)
    {
        BridgeClient = bridgeClient;
    }

    protected IBridgeClient BridgeClient { get; }
}
