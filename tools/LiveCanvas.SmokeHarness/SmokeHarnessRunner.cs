using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Bridge.Protocol.Responses;
using LiveCanvas.Bridge.Protocol.Serialization;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Contracts.Session;

namespace LiveCanvas.SmokeHarness;

public sealed record SmokeHarnessOptions(
    string AgentHostProjectPath,
    string? OutputDirectory = null,
    bool RunBridgeDirectCheck = true,
    bool RunMcpCheck = true,
    string Configuration = "Debug");

public sealed record SmokeHarnessResult(
    bool Success,
    string OutputDirectory,
    IReadOnlyList<string> CompletedChecks,
    IReadOnlyList<string> Errors);

public sealed class SmokeHarnessRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SmokeHarnessResult> RunAsync(SmokeHarnessOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AgentHostProjectPath);

        var outputDirectory = options.OutputDirectory
            ?? Path.Combine(Path.GetTempPath(), "livecanvas-smoke", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(outputDirectory);

        var completedChecks = new List<string>();
        var errors = new List<string>();

        try
        {
            await using var bridge = await MockBridgeServer.StartAsync(cancellationToken).ConfigureAwait(false);

            if (options.RunBridgeDirectCheck)
            {
                await RunBridgeDirectCheckAsync(bridge.BridgeUri, cancellationToken).ConfigureAwait(false);
                completedChecks.Add("bridge-jsonrpc");
            }

            if (options.RunMcpCheck)
            {
                await BuildAgentHostAsync(options.AgentHostProjectPath, options.Configuration, cancellationToken).ConfigureAwait(false);
                var agentHostDllPath = ResolveAgentHostDllPath(options.AgentHostProjectPath, options.Configuration);
                await RunMcpCheckAsync(agentHostDllPath, bridge.BridgeUri, outputDirectory, cancellationToken).ConfigureAwait(false);
                completedChecks.Add("mcp-stdio");
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        return new SmokeHarnessResult(errors.Count == 0, outputDirectory, completedChecks, errors);
    }

    private static async Task RunBridgeDirectCheckAsync(string bridgeUri, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(bridgeUri), cancellationToken).ConfigureAwait(false);

        var sessionJson = BridgeJsonSerializer.SerializeRequest("bridge-1", BridgeMethodNames.GhSessionInfo, new GhSessionInfoRequest());
        await SendWebSocketMessageAsync(socket, sessionJson, cancellationToken).ConfigureAwait(false);
        var sessionEnvelope = BridgeJsonSerializer.DeserializeResponse(
            await ReceiveWebSocketMessageAsync(socket, cancellationToken).ConfigureAwait(false));
        var session = BridgeJsonSerializer.DeserializeResult<GhSessionInfoResponse>(sessionEnvelope);

        if (!session.RhinoRunning || !session.GrasshopperLoaded)
        {
            throw new InvalidOperationException("Mock bridge did not return a healthy gh_session_info response.");
        }

        var newDocumentJson = BridgeJsonSerializer.SerializeRequest("bridge-2", BridgeMethodNames.GhNewDocument, new GhNewDocumentRequest("Bridge Smoke"));
        await SendWebSocketMessageAsync(socket, newDocumentJson, cancellationToken).ConfigureAwait(false);
        var newDocumentEnvelope = BridgeJsonSerializer.DeserializeResponse(
            await ReceiveWebSocketMessageAsync(socket, cancellationToken).ConfigureAwait(false));
        var newDocument = BridgeJsonSerializer.DeserializeResult<GhNewDocumentResponse>(newDocumentEnvelope);

        if (!newDocument.Cleared)
        {
            throw new InvalidOperationException("Mock bridge did not clear state on gh_new_document.");
        }

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunMcpCheckAsync(string agentHostDllPath, string bridgeUri, string outputDirectory, CancellationToken cancellationToken)
    {
        var processStartInfo = new ProcessStartInfo("dotnet", $"\"{agentHostDllPath}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        processStartInfo.Environment["LIVECANVAS_BRIDGE_URI"] = bridgeUri;

        using var process = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException("Failed to start the LiveCanvas.AgentHost process.");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await SendMcpMessageAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { }
        }, cancellationToken).ConfigureAwait(false);
        var initializeResponse = await ReadMcpResponseAsync(process.StandardOutput.BaseStream, cancellationToken).ConfigureAwait(false);
        EnsureMcpSuccess(initializeResponse, "initialize");

        await SendMcpMessageAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized",
            @params = new { }
        }, cancellationToken).ConfigureAwait(false);

        await SendMcpMessageAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list",
            @params = new { }
        }, cancellationToken).ConfigureAwait(false);
        var toolsResponse = await ReadMcpResponseAsync(process.StandardOutput.BaseStream, cancellationToken).ConfigureAwait(false);
        var listedTools = EnsureMcpSuccess(toolsResponse, "tools/list")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        var expectedTools = new[]
        {
            "gh_session_info",
            "gh_new_document",
            "gh_list_allowed_components",
            "gh_add_component",
            "gh_configure_component",
            "gh_connect",
            "gh_delete_component",
            "gh_solve",
            "gh_inspect_document",
            "gh_capture_preview",
            "gh_save_document"
        };

        if (!expectedTools.SequenceEqual(listedTools))
        {
            throw new InvalidOperationException("AgentHost tools/list response did not match the expected tool surface.");
        }

        var sessionInfo = await CallToolAsync(process, 3, "gh_session_info", new { }, cancellationToken).ConfigureAwait(false);
        if (!sessionInfo.GetProperty("rhinoRunning").GetBoolean())
        {
            throw new InvalidOperationException("gh_session_info returned an unhealthy mock bridge session.");
        }

        await CallToolAsync(process, 4, "gh_new_document", new { name = "Smoke MCP" }, cancellationToken).ConfigureAwait(false);
        await CallToolAsync(process, 5, "gh_list_allowed_components", new { }, cancellationToken).ConfigureAwait(false);

        var slider = await CallToolAsync(process, 6, "gh_add_component", new
        {
            component_key = "number_slider",
            x = 80,
            y = 80
        }, cancellationToken).ConfigureAwait(false);
        var sliderId = slider.GetProperty("componentId").GetString()
            ?? throw new InvalidOperationException("gh_add_component did not return a componentId for the slider.");

        await CallToolAsync(process, 7, "gh_configure_component", new
        {
            component_id = sliderId,
            config = new
            {
                slider = new
                {
                    min = 0,
                    max = 10,
                    value = 5,
                    integer = true
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        var plane = await CallToolAsync(process, 8, "gh_add_component", new
        {
            component_key = "xy_plane",
            x = 240,
            y = 80
        }, cancellationToken).ConfigureAwait(false);
        var planeId = plane.GetProperty("componentId").GetString()
            ?? throw new InvalidOperationException("gh_add_component did not return a componentId for XY Plane.");

        var rectangle = await CallToolAsync(process, 9, "gh_add_component", new
        {
            component_key = "rectangle",
            x = 400,
            y = 80
        }, cancellationToken).ConfigureAwait(false);
        var rectangleId = rectangle.GetProperty("componentId").GetString()
            ?? throw new InvalidOperationException("gh_add_component did not return a componentId for Rectangle.");

        await CallToolAsync(process, 10, "gh_connect", new
        {
            source_id = planeId,
            source_output = "P",
            target_id = rectangleId,
            target_input = "P"
        }, cancellationToken).ConfigureAwait(false);

        var inspect = await CallToolAsync(process, 11, "gh_inspect_document", new
        {
            include_connections = true,
            include_runtime_messages = true
        }, cancellationToken).ConfigureAwait(false);
        if (inspect.GetProperty("components").GetArrayLength() < 3)
        {
            throw new InvalidOperationException("gh_inspect_document did not report the expected smoke components.");
        }

        await CallToolAsync(process, 12, "gh_solve", new { expire_all = true }, cancellationToken).ConfigureAwait(false);
        await CallToolAsync(process, 13, "gh_delete_component", new { component_id = sliderId }, cancellationToken).ConfigureAwait(false);

        var previewPath = Path.Combine(outputDirectory, "preview.png");
        await CallToolAsync(process, 14, "gh_capture_preview", new
        {
            path = previewPath,
            width = 320,
            height = 200
        }, cancellationToken).ConfigureAwait(false);

        var savePath = Path.Combine(outputDirectory, "smoke.gh");
        await CallToolAsync(process, 15, "gh_save_document", new
        {
            path = savePath
        }, cancellationToken).ConfigureAwait(false);

        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask.ConfigureAwait(false);
            throw new InvalidOperationException($"LiveCanvas.AgentHost exited with code {process.ExitCode}. {stderr}".Trim());
        }
    }

    private static async Task<JsonElement> CallToolAsync(Process process, int id, string toolName, object arguments, CancellationToken cancellationToken)
    {
        await SendMcpMessageAsync(process.StandardInput.BaseStream, new
        {
            jsonrpc = "2.0",
            id,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments
            }
        }, cancellationToken).ConfigureAwait(false);

        var response = await ReadMcpResponseAsync(process.StandardOutput.BaseStream, cancellationToken).ConfigureAwait(false);
        return EnsureMcpSuccess(response, toolName).GetProperty("structuredContent");
    }

    private static async Task BuildAgentHostAsync(string agentHostProjectPath, string configuration, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet", $"build \"{agentHostProjectPath}\" --configuration {configuration} --nologo --verbosity quiet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet build for LiveCanvas.AgentHost.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"dotnet build failed for LiveCanvas.AgentHost.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}".Trim());
        }
    }

    private static string ResolveAgentHostDllPath(string agentHostProjectPath, string configuration)
    {
        var projectDirectory = Path.GetDirectoryName(agentHostProjectPath)
            ?? throw new ArgumentException("AgentHost project path must include a directory.", nameof(agentHostProjectPath));
        var dllPath = Path.Combine(projectDirectory, "bin", configuration, "net8.0", "LiveCanvas.AgentHost.dll");

        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException("Could not find the built LiveCanvas.AgentHost.dll.", dllPath);
        }

        return dllPath;
    }

    private static JsonElement EnsureMcpSuccess(JsonDocument response, string methodName)
    {
        var root = response.RootElement;
        if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null)
        {
            throw new InvalidOperationException($"{methodName} failed: {errorElement.GetProperty("message").GetString()}");
        }

        return root.GetProperty("result");
    }

    private static async Task SendMcpMessageAsync(Stream output, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadMcpResponseAsync(Stream input, CancellationToken cancellationToken)
    {
        var headerBytes = new List<byte>();
        var singleByte = new byte[1];

        while (true)
        {
            var read = await input.ReadAsync(singleByte, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("AgentHost closed stdout before a complete MCP response was received.");
            }

            headerBytes.Add(singleByte[0]);
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
        var body = new byte[contentLength];
        await ReadExactlyAsync(input, body, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(body);
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

        throw new InvalidOperationException("Missing Content-Length header in MCP response.");
    }

    private static async Task ReadExactlyAsync(Stream input, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var bytesRead = await input.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading MCP response.");
            }

            totalRead += bytesRead;
        }
    }

    private static Task SendWebSocketMessageAsync(ClientWebSocket socket, string payload, CancellationToken cancellationToken)
    {
        var buffer = Encoding.UTF8.GetBytes(payload);
        return socket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string> ReceiveWebSocketMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var payload = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Bridge closed before returning a response.");
            }

            payload.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(payload.ToArray());
    }
}
