using System.Text;
using System.Text.Json;
using LiveCanvas.AgentHost.BridgeClient;

namespace LiveCanvas.AgentHost.Mcp;

internal sealed class StdioMcpServer
{
    private readonly McpToolExecutor toolExecutor;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public StdioMcpServer(McpToolExecutor toolExecutor)
    {
        this.toolExecutor = toolExecutor;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var input = Console.OpenStandardInput();
        var output = Console.OpenStandardOutput();

        while (!cancellationToken.IsCancellationRequested)
        {
            var requestJson = await ReadMessageAsync(input, cancellationToken);
            if (requestJson is null)
            {
                break;
            }

            var request = JsonSerializer.Deserialize<McpRequest>(requestJson, jsonOptions)
                ?? throw new InvalidOperationException("Could not deserialize MCP request.");

            var response = await HandleRequestAsync(request, cancellationToken);
            if (response is null)
            {
                continue;
            }

            var responseJson = JsonSerializer.Serialize(response, jsonOptions);
            await WriteMessageAsync(output, responseJson, cancellationToken);
        }
    }

    private async Task<McpResponse?> HandleRequestAsync(McpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return request.Method switch
            {
                "initialize" => Ok(
                    request.Id,
                    new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new { tools = new { } },
                        serverInfo = new
                        {
                            name = "LiveCanvas.AgentHost",
                            version = "0.1.0"
                        }
                    }),
                "notifications/initialized" => null,
                "tools/list" => Ok(request.Id, new { tools = McpToolCatalog.All }),
                "tools/call" => await HandleToolsCallAsync(request, cancellationToken),
                _ => Error(request.Id, -32601, $"Method '{request.Method}' was not found.")
            };
        }
        catch (BridgeClientUnavailableException ex)
        {
            return Error(request.Id, -32001, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Error(request.Id, -32602, ex.Message);
        }
        catch (Exception ex)
        {
            return Error(request.Id, -32603, ex.Message);
        }
    }

    private async Task<McpResponse> HandleToolsCallAsync(McpRequest request, CancellationToken cancellationToken)
    {
        if (request.Params is null)
        {
            throw new ArgumentException("tools/call requires params.");
        }

        var @params = request.Params.Value;
        var toolName = @params.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("tools/call requires a tool name.");
        }

        var arguments = @params.TryGetProperty("arguments", out var argumentsElement) && argumentsElement.ValueKind == JsonValueKind.Object
            ? argumentsElement
            : JsonDocument.Parse("{}").RootElement.Clone();

        var result = await toolExecutor.InvokeAsync(toolName, arguments, cancellationToken);
        return Ok(
            request.Id,
            new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(result, jsonOptions)
                    }
                },
                structuredContent = result,
                isError = false
            });
    }

    private static McpResponse Ok(JsonElement? id, object result) =>
        new("2.0", id, result, null);

    private static McpResponse Error(JsonElement? id, int code, string message) =>
        new("2.0", id, null, new McpError(code, message));

    private static async Task<string?> ReadMessageAsync(Stream input, CancellationToken cancellationToken)
    {
        var headerBytes = new List<byte>();
        var readBuffer = new byte[1];

        while (true)
        {
            var read = await input.ReadAsync(readBuffer, cancellationToken);
            if (read == 0)
            {
                return null;
            }

            headerBytes.Add(readBuffer[0]);
            var count = headerBytes.Count;
            if (count >= 4
                && headerBytes[count - 4] == '\r'
                && headerBytes[count - 3] == '\n'
                && headerBytes[count - 2] == '\r'
                && headerBytes[count - 1] == '\n')
            {
                break;
            }
        }

        var headers = Encoding.ASCII.GetString(headerBytes.ToArray());
        var contentLength = ParseContentLength(headers);
        var payload = new byte[contentLength];
        await ReadExactlyAsync(input, payload, cancellationToken);
        return Encoding.UTF8.GetString(payload);
    }

    private static async Task WriteMessageAsync(Stream output, string payload, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(payload);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await output.WriteAsync(header, cancellationToken);
        await output.WriteAsync(body, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static int ParseContentLength(string headers)
    {
        foreach (var line in headers.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line["Content-Length:".Length..].Trim(), out var contentLength))
            {
                return contentLength;
            }
        }

        throw new InvalidOperationException("Missing Content-Length header.");
    }

    private static async Task ReadExactlyAsync(Stream input, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var bytesRead = await input.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading MCP payload.");
            }

            totalRead += bytesRead;
        }
    }
}
