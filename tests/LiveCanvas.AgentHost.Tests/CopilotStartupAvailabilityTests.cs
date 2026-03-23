using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using LiveCanvas.AgentHost.Mcp;
using LiveCanvas.AgentHost.Startup;

namespace LiveCanvas.AgentHost.Tests;

public class CopilotStartupAvailabilityTests
{
    [Fact]
    public async Task startup_copilot_plan_unavailable_error_is_explicit_and_not_internal_error()
    {
        var server = new AgentHostCompositionRoot().CreateServer();
        var request = new McpRequest(
            Jsonrpc: "2.0",
            Id: JsonDocument.Parse("1").RootElement.Clone(),
            Method: "tools/call",
            Params: JsonDocument.Parse("""{"name":"copilot_plan","arguments":{"prompt":"design a tower"}}""").RootElement.Clone());

        var response = await InvokeHandleRequestAsync(server, request);

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().NotBe(-32603);
        response.Error.Message.Should().Contain("copilot_plan");
        response.Error.Message.Should().Contain("unavailable");
    }

    private static async Task<McpResponse> InvokeHandleRequestAsync(StdioMcpServer server, McpRequest request)
    {
        var method = typeof(StdioMcpServer).GetMethod("HandleRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find HandleRequestAsync.");

        var task = method.Invoke(server, [request, CancellationToken.None]) as Task<McpResponse?>;
        if (task is null)
        {
            throw new InvalidOperationException("Could not invoke HandleRequestAsync.");
        }

        var response = await task;
        return response ?? throw new InvalidOperationException("Expected a non-null MCP response.");
    }
}
