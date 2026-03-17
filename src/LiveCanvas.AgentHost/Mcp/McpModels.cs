using System.Text.Json;

namespace LiveCanvas.AgentHost.Mcp;

internal sealed record McpRequest(
    string Jsonrpc,
    JsonElement? Id,
    string Method,
    JsonElement? Params);

internal sealed record McpError(
    int Code,
    string Message);

internal sealed record McpResponse(
    string Jsonrpc,
    JsonElement? Id,
    object? Result,
    McpError? Error);

internal sealed record McpToolDescriptor(
    string Name,
    string Description,
    object InputSchema);
