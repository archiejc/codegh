namespace LiveCanvas.AgentHost.Mcp;

internal sealed class McpToolUnavailableException(string toolName, string message)
    : Exception(message)
{
    public string ToolName { get; } = toolName;
}
