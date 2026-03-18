using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Bridge.Protocol.Serialization;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Contracts.Session;

namespace LiveCanvas.SmokeHarness;

public sealed class SmokeHarnessRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyList<string> ExpectedTools =
    [
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
    ];

    private static readonly IReadOnlyList<SmokeGraphComponentDefinition> SmokeComponents =
    [
        new("width_slider", "number_slider", 40, 20, "Width", new GhComponentConfig(Slider: new SliderConfig(1, 50, 20, false))),
        new("depth_slider", "number_slider", 40, 70, "Depth", new GhComponentConfig(Slider: new SliderConfig(1, 50, 12, false))),
        new("height_slider", "number_slider", 760, 20, "Height", new GhComponentConfig(Slider: new SliderConfig(1, 80, 18, false))),
        new("xy_plane", "xy_plane", 40, 160, "XY", new GhComponentConfig(Nickname: "XY")),
        new("width_domain", "construct_domain", 260, 20, "Width Domain", new GhComponentConfig(Nickname: "W Domain")),
        new("depth_domain", "construct_domain", 260, 70, "Depth Domain", new GhComponentConfig(Nickname: "D Domain")),
        new("rectangle", "rectangle", 500, 160, "Rect", new GhComponentConfig(Nickname: "Rect")),
        new("boundary_surfaces", "boundary_surfaces", 740, 160, "Brep", new GhComponentConfig(Nickname: "Brep")),
        new("vector_xyz", "vector_xyz", 980, 160, "Vec", new GhComponentConfig(Nickname: "Vec")),
        new("extrude", "extrude", 1220, 160, "Extrude", new GhComponentConfig(Nickname: "Extrude"))
    ];

    private static readonly IReadOnlyList<SmokeGraphConnectionDefinition> SmokeConnections =
    [
        new("xy_plane", "P", "rectangle", "P"),
        new("width_slider", "N", "width_domain", "E"),
        new("depth_slider", "N", "depth_domain", "E"),
        new("width_domain", "I", "rectangle", "X"),
        new("depth_domain", "I", "rectangle", "Y"),
        new("rectangle", "R", "boundary_surfaces", "E"),
        new("boundary_surfaces", "S", "extrude", "B"),
        new("height_slider", "N", "vector_xyz", "Z"),
        new("vector_xyz", "V", "extrude", "D")
    ];

    public async Task<SmokeHarnessResult> RunAsync(SmokeHarnessOptions options, CancellationToken cancellationToken = default)
    {
        var outputDirectory = options.OutputDirectory
            ?? Path.Combine(Path.GetTempPath(), "livecanvas-smoke", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(outputDirectory);
        var context = new HarnessRunContext(options, outputDirectory, JsonOptions);

        try
        {
            ValidateOptions(options);

            if (options.Mode == SmokeHarnessMode.Mock)
            {
                await using var bridge = await MockBridgeServer.StartAsync(cancellationToken).ConfigureAwait(false);
                context.BridgeUri = bridge.BridgeUri;
                await RunChecksAsync(context, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                context.BridgeUri = options.BridgeUri ?? BridgeDefaults.WebSocketUri;
                await RunChecksAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            context.AddError(NormalizeError(ex));
            context.AddEvent("failure", "runner", "run", new { mode = options.Mode.ToString().ToLowerInvariant() }, new { error = ex.Message }, false);
        }
        finally
        {
            context.FinishedUtc = DateTimeOffset.UtcNow;
            await WriteOutputsAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return new SmokeHarnessResult(
            context.Errors.Count == 0,
            context.OutputDirectory,
            context.CompletedChecks,
            context.Errors);
    }

    private static void ValidateOptions(SmokeHarnessOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AgentHostProjectPath);

        if (options.Mode == SmokeHarnessMode.Mock && options.BridgeUri is not null)
        {
            throw new SmokeHarnessFailureException("cli_usage", "--bridge-uri is only valid in --mode live.");
        }

        if (options.Mode == SmokeHarnessMode.Mock && options.LivePreflightTimeoutSeconds != 10)
        {
            throw new SmokeHarnessFailureException("cli_usage", "--live-preflight-timeout-seconds is only valid in --mode live.");
        }

        if (options.Mode == SmokeHarnessMode.Live
            && options.BridgeUri is not null
            && (!Uri.TryCreate(options.BridgeUri, UriKind.Absolute, out var bridgeUri) || !string.Equals(bridgeUri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)))
        {
            throw new SmokeHarnessFailureException("cli_usage", $"Malformed live bridge URI '{options.BridgeUri}'. Expected a ws:// URI.");
        }

        if (!options.RunBridgeDirectCheck && !options.RunMcpCheck)
        {
            throw new SmokeHarnessFailureException("cli_usage", "At least one of the bridge or MCP checks must be enabled.");
        }
    }

    private static async Task RunChecksAsync(HarnessRunContext context, CancellationToken cancellationToken)
    {
        var bridgeUri = context.BridgeUri ?? throw new InvalidOperationException("Bridge URI was not resolved.");

        if (context.Options.RunBridgeDirectCheck)
        {
            var session = await RunBridgeDirectCheckAsync(context, bridgeUri, cancellationToken).ConfigureAwait(false);
            context.SessionSummary = new SmokeSessionSummary(session.Platform, session.RhinoVersion, session.ToolVersion);
            context.AddCompletedCheck($"bridge-jsonrpc-{context.Options.Mode.ToString().ToLowerInvariant()}");
        }

        if (context.Options.RunMcpCheck)
        {
            var agentHostDllPath = await ResolveAgentHostDllPathAsync(context, cancellationToken).ConfigureAwait(false);
            var session = await RunMcpCheckAsync(context, agentHostDllPath, bridgeUri, cancellationToken).ConfigureAwait(false);
            context.SessionSummary ??= new SmokeSessionSummary(session.Platform, session.RhinoVersion, session.ToolVersion);
            context.AddCompletedCheck($"mcp-stdio-{context.Options.Mode.ToString().ToLowerInvariant()}");
        }
    }

    private static async Task<GhSessionInfoResponse> RunBridgeDirectCheckAsync(HarnessRunContext context, string bridgeUri, CancellationToken cancellationToken)
    {
        CancellationTokenSource? linkedCts = null;
        try
        {
            if (context.Options.Mode == SmokeHarnessMode.Live)
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(context.Options.LivePreflightTimeoutSeconds));
                cancellationToken = linkedCts.Token;
            }

            using var socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(new Uri(bridgeUri), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var category = context.Options.Mode == SmokeHarnessMode.Live ? "bridge_unreachable" : "bridge_protocol_error";
                throw new SmokeHarnessFailureException(category, $"Could not connect to bridge '{bridgeUri}'.", ex);
            }

            var request = BridgeJsonSerializer.SerializeRequest("bridge-session-info", BridgeMethodNames.GhSessionInfo, new GhSessionInfoRequest());
            context.AddEvent("bridge-preflight", "websocket", BridgeMethodNames.GhSessionInfo, ParseJson(request), null, true);

            var responseJson = await RoundTripWebSocketAsync(socket, request, cancellationToken).ConfigureAwait(false);
            var envelope = BridgeJsonSerializer.DeserializeResponse(responseJson);
            context.AddEvent("bridge-preflight", "websocket", BridgeMethodNames.GhSessionInfo, null, ParseJson(responseJson), envelope.Error is null);

            var session = BridgeJsonSerializer.DeserializeResult<GhSessionInfoResponse>(envelope);
            if (!session.RhinoRunning || !session.GrasshopperLoaded)
            {
                var category = context.Options.Mode == SmokeHarnessMode.Live ? "live_precondition_failed" : "bridge_protocol_error";
                throw new SmokeHarnessFailureException(category, "gh_session_info did not report a healthy Rhino and Grasshopper session.");
            }

            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cancellationToken).ConfigureAwait(false);
            return session;
        }
        finally
        {
            linkedCts?.Dispose();
        }
    }

    private static async Task<GhSessionInfoResponse> RunMcpCheckAsync(
        HarnessRunContext context,
        string agentHostDllPath,
        string bridgeUri,
        CancellationToken cancellationToken)
    {
        var processStartInfo = new ProcessStartInfo("dotnet", $"\"{agentHostDllPath}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        processStartInfo.Environment["LIVECANVAS_BRIDGE_URI"] = bridgeUri;

        Process process;
        try
        {
            process = Process.Start(processStartInfo)
                ?? throw new InvalidOperationException("Failed to start the LiveCanvas.AgentHost process.");
        }
        catch (Exception ex)
        {
            throw new SmokeHarnessFailureException("agenthost_start_failed", "Failed to start the LiveCanvas.AgentHost process.", ex);
        }

        context.AddEvent("process", "process", "agenthost_start", new { agentHostDllPath, bridgeUri }, new { pid = process.Id }, true);

        using var processHandle = process;
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var exitCode = 0;
        string? stderr = null;

        try
        {
            await SendMcpMessageAndReadResultAsync(process, context, 1, "initialize", new { }, cancellationToken).ConfigureAwait(false);

            await SendMcpNotificationAsync(process, context, "notifications/initialized", new { }, cancellationToken).ConfigureAwait(false);

            var toolsResult = await SendMcpMessageAndReadResultAsync(process, context, 2, "tools/list", new { }, cancellationToken).ConfigureAwait(false);
            var listedTools = toolsResult
                .GetProperty("tools")
                .EnumerateArray()
                .Select(item => item.GetProperty("name").GetString())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();

            if (!ExpectedTools.SequenceEqual(listedTools))
            {
                throw new SmokeHarnessFailureException("tool_surface_mismatch", "AgentHost tools/list response did not match the expected v0 tool surface.");
            }

            var sessionInfoJson = await CallToolAsync(process, context, 3, "gh_session_info", new { }, cancellationToken).ConfigureAwait(false);
            var sessionInfo = Deserialize<GhSessionInfoResponse>(sessionInfoJson);
            context.SessionSummary = new SmokeSessionSummary(sessionInfo.Platform, sessionInfo.RhinoVersion, sessionInfo.ToolVersion);
            if (!sessionInfo.RhinoRunning || !sessionInfo.GrasshopperLoaded)
            {
                throw new SmokeHarnessFailureException("live_precondition_failed", "gh_session_info did not report a healthy session.");
            }

            _ = await CallToolAsync(process, context, 4, "gh_new_document", new { name = "LiveCanvas Smoke" }, cancellationToken).ConfigureAwait(false);

            var allowedComponentsJson = await CallToolAsync(process, context, 5, "gh_list_allowed_components", new { }, cancellationToken).ConfigureAwait(false);
            var allowedComponents = Deserialize<GhListAllowedComponentsResponse>(allowedComponentsJson);
            ValidateAllowedComponents(allowedComponents);

            var componentIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var requestId = 6;

            foreach (var component in SmokeComponents)
            {
                var addResponseJson = await CallToolAsync(process, context, requestId++, "gh_add_component", new
                {
                    component_key = component.ComponentKey,
                    x = component.X,
                    y = component.Y
                }, cancellationToken).ConfigureAwait(false);

                var addResponse = Deserialize<GhAddComponentResponse>(addResponseJson);
                componentIds[component.Alias] = addResponse.ComponentId;

                var configPayload = new
                {
                    component_id = addResponse.ComponentId,
                    config = component.Config with { Nickname = component.Nickname }
                };

                _ = await CallToolAsync(process, context, requestId++, "gh_configure_component", configPayload, cancellationToken).ConfigureAwait(false);
            }

            foreach (var connection in SmokeConnections)
            {
                _ = await CallToolAsync(process, context, requestId++, "gh_connect", new
                {
                    source_id = componentIds[connection.SourceAlias],
                    source_output = connection.SourceOutput,
                    target_id = componentIds[connection.TargetAlias],
                    target_input = connection.TargetInput
                }, cancellationToken).ConfigureAwait(false);
            }

            var inspectBeforeJson = await CallToolAsync(process, context, requestId++, "gh_inspect_document", new
            {
                include_connections = true,
                include_runtime_messages = true
            }, cancellationToken).ConfigureAwait(false);
            var inspectBefore = Deserialize<GhInspectDocumentResponse>(inspectBeforeJson);
            EnsureExpectedCounts(inspectBefore);
            RecordWarnings(context, inspectBefore.RuntimeMessages, "inspect_before_solve");

            var solveJson = await CallToolAsync(process, context, requestId++, "gh_solve", new { expire_all = true }, cancellationToken).ConfigureAwait(false);
            var solve = Deserialize<GhSolveResponse>(solveJson);
            RecordWarnings(context, solve.Messages, "solve");
            if (!solve.Solved || solve.ErrorCount > 0 || string.Equals(solve.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw new SmokeHarnessFailureException("tool_call_failed", "gh_solve did not complete successfully.");
            }

            var inspectAfterJson = await CallToolAsync(process, context, requestId++, "gh_inspect_document", new
            {
                include_connections = true,
                include_runtime_messages = true
            }, cancellationToken).ConfigureAwait(false);
            var inspectAfter = Deserialize<GhInspectDocumentResponse>(inspectAfterJson);
            EnsureExpectedCounts(inspectAfter);
            RecordWarnings(context, inspectAfter.RuntimeMessages, "inspect_after_solve");

            if (!inspectAfter.PreviewSummary.HasGeometry || inspectAfter.PreviewSummary.PreviewObjectCount <= 0 || inspectAfter.BoundingBox is null)
            {
                throw new SmokeHarnessFailureException("artifact_missing", "gh_inspect_document did not report geometry preview artifacts.");
            }

            var captureJson = await CallToolAsync(process, context, requestId++, "gh_capture_preview", new
            {
                path = context.PreviewPath,
                width = 640,
                height = 360
            }, cancellationToken).ConfigureAwait(false);
            var capture = Deserialize<GhCapturePreviewResponse>(captureJson);
            if (!capture.Captured)
            {
                throw new SmokeHarnessFailureException("artifact_missing", "gh_capture_preview did not report a captured image.");
            }

            EnsureArtifactExists(context.PreviewPath, "preview.png");
            context.AddEvent("artifact", "filesystem", "preview_write", new { path = context.PreviewPath }, new { bytes = new FileInfo(context.PreviewPath).Length }, true);

            var saveJson = await CallToolAsync(process, context, requestId++, "gh_save_document", new
            {
                path = context.GhPath
            }, cancellationToken).ConfigureAwait(false);
            var save = Deserialize<GhSaveDocumentResponse>(saveJson);
            if (!save.Saved || !string.Equals(Path.GetExtension(save.Path), ".gh", StringComparison.OrdinalIgnoreCase))
            {
                throw new SmokeHarnessFailureException("artifact_missing", "gh_save_document did not report a valid .gh save.");
            }

            EnsureArtifactExists(context.GhPath, "smoke.gh");
            context.AddEvent("artifact", "filesystem", "gh_write", new { path = context.GhPath }, new { bytes = new FileInfo(context.GhPath).Length }, true);
        }
        finally
        {
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
            exitCode = process.ExitCode;
            context.AddEvent("process", "process", "agenthost_stop", null, new { exitCode, stderr }, exitCode == 0);
        }

        if (exitCode != 0)
        {
            throw new SmokeHarnessFailureException("agenthost_start_failed", $"LiveCanvas.AgentHost exited with code {exitCode}. {stderr}".Trim());
        }

        return context.SessionSummary is null
            ? new GhSessionInfoResponse(true, null, "unknown", true, null, SmokeComponents.Count, "Meters", 0.01, "unknown")
            : new GhSessionInfoResponse(true, context.SessionSummary.RhinoVersion, context.SessionSummary.Platform, true, null, SmokeComponents.Count, "Meters", 0.01, context.SessionSummary.ToolVersion);
    }

    private static async Task<string> ResolveAgentHostDllPathAsync(HarnessRunContext context, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(context.Options.AgentHostDllPath))
        {
            EnsureFileExists(context.Options.AgentHostDllPath!, "agenthost_start_failed", "Configured AgentHost DLL path was not found.");
            return context.Options.AgentHostDllPath!;
        }

        if (!context.Options.SkipBuildAgentHost)
        {
            await BuildAgentHostAsync(context.Options.AgentHostProjectPath, context.Options.Configuration, cancellationToken).ConfigureAwait(false);
        }

        var dllPath = ResolveAgentHostDllPath(context.Options.AgentHostProjectPath, context.Options.Configuration);
        EnsureFileExists(dllPath, "agenthost_start_failed", "Could not find the built LiveCanvas.AgentHost.dll.");
        return dllPath;
    }

    private static void ValidateAllowedComponents(GhListAllowedComponentsResponse response)
    {
        var available = response.Components.Select(component => component.ComponentKey).ToHashSet(StringComparer.Ordinal);
        var missing = SmokeComponents
            .Select(component => component.ComponentKey)
            .Where(componentKey => !available.Contains(componentKey))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(componentKey => componentKey, StringComparer.Ordinal)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new SmokeHarnessFailureException("tool_call_failed", $"gh_list_allowed_components is missing required smoke graph keys: {string.Join(", ", missing)}.");
        }
    }

    private static void EnsureExpectedCounts(GhInspectDocumentResponse inspect)
    {
        if (inspect.Components.Count != SmokeComponents.Count)
        {
            throw new SmokeHarnessFailureException("tool_call_failed", $"gh_inspect_document reported {inspect.Components.Count} components; expected {SmokeComponents.Count}.");
        }

        if (inspect.Connections.Count != SmokeConnections.Count)
        {
            throw new SmokeHarnessFailureException("tool_call_failed", $"gh_inspect_document reported {inspect.Connections.Count} connections; expected {SmokeConnections.Count}.");
        }
    }

    private static void RecordWarnings(HarnessRunContext context, IEnumerable<GhRuntimeMessage> messages, string phase)
    {
        foreach (var message in messages.Where(message => string.Equals(message.Level, "warning", StringComparison.OrdinalIgnoreCase)))
        {
            context.AddWarning($"{phase}: {message.Text}");
        }
    }

    private static void EnsureArtifactExists(string path, string artifactName)
    {
        EnsureFileExists(path, "artifact_missing", $"Expected artifact '{artifactName}' at '{path}' was not found.");
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length <= 0)
        {
            throw new SmokeHarnessFailureException("artifact_missing", $"Expected artifact '{artifactName}' at '{path}' to be non-empty.");
        }
    }

    private static void EnsureFileExists(string path, string category, string message)
    {
        if (!File.Exists(path))
        {
            throw new SmokeHarnessFailureException(category, message);
        }
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
            ?? throw new SmokeHarnessFailureException("agenthost_build_failed", "Failed to start dotnet build for LiveCanvas.AgentHost.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new SmokeHarnessFailureException("agenthost_build_failed", $"dotnet build failed for LiveCanvas.AgentHost.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}".Trim());
        }
    }

    private static string ResolveAgentHostDllPath(string agentHostProjectPath, string configuration)
    {
        var projectDirectory = Path.GetDirectoryName(agentHostProjectPath)
            ?? throw new ArgumentException("AgentHost project path must include a directory.", nameof(agentHostProjectPath));
        return Path.Combine(projectDirectory, "bin", configuration, "net8.0", "LiveCanvas.AgentHost.dll");
    }

    private static async Task<JsonElement> SendMcpMessageAndReadResultAsync(
        Process process,
        HarnessRunContext context,
        int id,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        };

        var requestJson = JsonSerializer.Serialize(payload, JsonOptions);
        context.AddEvent("mcp", "stdio", method, ParseJson(requestJson), null, true);
        await SendMcpMessageAsync(process.StandardInput.BaseStream, requestJson, cancellationToken).ConfigureAwait(false);

        var response = await ReadMcpResponseAsync(process.StandardOutput.BaseStream, cancellationToken).ConfigureAwait(false);
        var success = !response.RootElement.TryGetProperty("error", out var errorElement) || errorElement.ValueKind == JsonValueKind.Null;
        context.AddEvent("mcp", "stdio", method, null, response.RootElement.Clone(), success);

        try
        {
            return EnsureMcpSuccess(response, method);
        }
        catch (Exception ex)
        {
            throw new SmokeHarnessFailureException("mcp_protocol_error", ex.Message, ex);
        }
    }

    private static async Task SendMcpNotificationAsync(
        Process process,
        HarnessRunContext context,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        };

        var requestJson = JsonSerializer.Serialize(payload, JsonOptions);
        context.AddEvent("mcp", "stdio", method, ParseJson(requestJson), null, true);
        await SendMcpMessageAsync(process.StandardInput.BaseStream, requestJson, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonElement> CallToolAsync(
        Process process,
        HarnessRunContext context,
        int id,
        string toolName,
        object arguments,
        CancellationToken cancellationToken)
    {
        var result = await SendMcpMessageAndReadResultAsync(process, context, id, "tools/call", new
        {
            name = toolName,
            arguments
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            return result.GetProperty("structuredContent").Clone();
        }
        catch (Exception ex)
        {
            throw new SmokeHarnessFailureException("tool_call_failed", $"Tool '{toolName}' did not return structuredContent.", ex);
        }
    }

    private static JsonElement EnsureMcpSuccess(JsonDocument response, string methodName)
    {
        var root = response.RootElement;
        if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null)
        {
            throw new InvalidOperationException($"{methodName} failed: {errorElement.GetProperty("message").GetString()}");
        }

        return root.GetProperty("result").Clone();
    }

    private static T Deserialize<T>(JsonElement value) =>
        value.Deserialize<T>(JsonOptions)
        ?? throw new InvalidOperationException($"Failed to deserialize harness payload as {typeof(T).Name}.");

    private static async Task SendMcpMessageAsync(Stream output, string json, CancellationToken cancellationToken)
    {
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
                throw new SmokeHarnessFailureException("mcp_protocol_error", "AgentHost closed stdout before a complete MCP response was received.");
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

        throw new SmokeHarnessFailureException("mcp_protocol_error", "Missing Content-Length header in MCP response.");
    }

    private static async Task ReadExactlyAsync(Stream input, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var bytesRead = await input.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new SmokeHarnessFailureException("mcp_protocol_error", "Unexpected end of stream while reading MCP response.");
            }

            totalRead += bytesRead;
        }
    }

    private static async Task<string> RoundTripWebSocketAsync(ClientWebSocket socket, string payload, CancellationToken cancellationToken)
    {
        var requestBytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(requestBytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        return await ReceiveWebSocketMessageAsync(socket, cancellationToken).ConfigureAwait(false);
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
                throw new SmokeHarnessFailureException("bridge_protocol_error", "Bridge closed before returning a response.");
            }

            payload.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(payload.ToArray());
    }

    private static JsonElement ParseJson(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string NormalizeError(Exception exception) =>
        exception switch
        {
            SmokeHarnessFailureException typed => typed.Message,
            _ => $"tool_call_failed: {exception.Message}"
        };

    private static async Task WriteOutputsAsync(HarnessRunContext context, CancellationToken cancellationToken)
    {
        var transcriptJson = JsonSerializer.Serialize(context.Transcript, JsonOptions);
        await File.WriteAllTextAsync(context.TranscriptPath, transcriptJson, cancellationToken).ConfigureAwait(false);

        var manifest = new SmokeArtifactManifest(
            Mode: context.Options.Mode.ToString().ToLowerInvariant(),
            BridgeUri: context.BridgeUri ?? string.Empty,
            OutputDirectory: context.OutputDirectory,
            PreviewPath: context.PreviewPath,
            GhPath: context.GhPath,
            TranscriptPath: context.TranscriptPath,
            CompletedChecks: context.CompletedChecks.ToArray(),
            Success: context.Errors.Count == 0,
            Errors: context.Errors.ToArray(),
            Warnings: context.Warnings.ToArray(),
            StartTimeUtc: context.StartedUtc,
            FinishTimeUtc: context.FinishedUtc ?? DateTimeOffset.UtcNow,
            SessionSummary: context.SessionSummary);

        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(context.ManifestPath, manifestJson, cancellationToken).ConfigureAwait(false);
    }

    private sealed record SmokeGraphComponentDefinition(
        string Alias,
        string ComponentKey,
        double X,
        double Y,
        string Nickname,
        GhComponentConfig Config);

    private sealed record SmokeGraphConnectionDefinition(
        string SourceAlias,
        string SourceOutput,
        string TargetAlias,
        string TargetInput);
}
