using LiveCanvas.AgentHost.Mcp;

namespace LiveCanvas.AgentHost.Startup;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var server = new AgentHostCompositionRoot().CreateServer();
        await server.RunAsync();
    }
}
