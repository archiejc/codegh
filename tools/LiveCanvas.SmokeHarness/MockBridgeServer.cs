using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Bridge.Protocol.Serialization;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Contracts.Session;
using LiveCanvas.Core.AllowedComponents;

namespace LiveCanvas.SmokeHarness;

public sealed class MockBridgeServer : IAsyncDisposable
{
    private static readonly byte[] PreviewPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j5wsAAAAASUVORK5CYII=");

    private readonly HttpListener listener;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task acceptLoop;
    private readonly AllowedComponentRegistry allowedComponents = new();
    private readonly Dictionary<string, MockComponent> components = new(StringComparer.Ordinal);
    private readonly List<GhDocumentConnectionSnapshot> connections = [];
    private int nextComponentId = 1;
    private int nextDocumentId = 1;
    private string? activeDocumentId;
    private string? activeDocumentName;

    private MockBridgeServer(HttpListener listener, int port)
    {
        this.listener = listener;
        Port = port;
        BridgeUri = $"ws://127.0.0.1:{port}{BridgeDefaults.WebSocketPath}";
        acceptLoop = Task.Run(() => AcceptLoopAsync(shutdown.Token));
    }

    public int Port { get; }

    public string BridgeUri { get; }

    public static Task<MockBridgeServer> StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var port = GetAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/livecanvas/");
        listener.Start();

        return Task.FromResult(new MockBridgeServer(listener, port));
    }

    public async ValueTask DisposeAsync()
    {
        shutdown.Cancel();
        listener.Stop();

        try
        {
            await acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException)
        {
        }
        finally
        {
            listener.Close();
            shutdown.Dispose();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() => HandleContextAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsWebSocketRequest || context.Request.Url?.AbsolutePath != BridgeDefaults.WebSocketPath)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.Close();
            return;
        }

        WebSocketContext webSocketContext;

        try
        {
            webSocketContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
        }
        catch (Exception)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.Close();
            return;
        }

        using var socket = webSocketContext.WebSocket;
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var requestJson = await ReceiveMessageAsync(socket, cancellationToken).ConfigureAwait(false);
            if (requestJson is null)
            {
                break;
            }

            var responseJson = Dispatch(requestJson);
            await SendMessageAsync(socket, responseJson, cancellationToken).ConfigureAwait(false);
        }
    }

    private string Dispatch(string requestJson)
    {
        var envelope = BridgeJsonSerializer.DeserializeRequest(requestJson);

        try
        {
            object response = envelope.Method switch
            {
                BridgeMethodNames.GhSessionInfo => new GhSessionInfoResponse(
                    RhinoRunning: true,
                    RhinoVersion: "8.0-mock",
                    Platform: OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsWindows() ? "windows" : "unknown",
                    GrasshopperLoaded: true,
                    ActiveDocumentName: activeDocumentName,
                    DocumentObjectCount: components.Count,
                    Units: "Meters",
                    ModelTolerance: 0.01,
                    ToolVersion: "0.1.0-smoke"),
                BridgeMethodNames.GhNewDocument => HandleNewDocument(BridgeJsonSerializer.DeserializeParams<GhNewDocumentRequest>(envelope.Params)),
                BridgeMethodNames.GhListAllowedComponents => new GhListAllowedComponentsResponse(allowedComponents.All()),
                BridgeMethodNames.GhAddComponent => HandleAddComponent(BridgeJsonSerializer.DeserializeParams<GhAddComponentRequest>(envelope.Params)),
                BridgeMethodNames.GhConfigureComponent => HandleConfigureComponent(BridgeJsonSerializer.DeserializeParams<GhConfigureComponentRequest>(envelope.Params)),
                BridgeMethodNames.GhConnect => HandleConnect(BridgeJsonSerializer.DeserializeParams<GhConnectRequest>(envelope.Params)),
                BridgeMethodNames.GhDeleteComponent => HandleDeleteComponent(BridgeJsonSerializer.DeserializeParams<GhDeleteComponentRequest>(envelope.Params)),
                BridgeMethodNames.GhSolve => new GhSolveResponse(true, "ok", components.Count, 0, 0, []),
                BridgeMethodNames.GhInspectDocument => HandleInspectDocument(BridgeJsonSerializer.DeserializeParams<GhInspectDocumentRequest>(envelope.Params)),
                BridgeMethodNames.GhCapturePreview => HandleCapturePreview(BridgeJsonSerializer.DeserializeParams<GhCapturePreviewRequest>(envelope.Params)),
                BridgeMethodNames.GhSaveDocument => HandleSaveDocument(BridgeJsonSerializer.DeserializeParams<GhSaveDocumentRequest>(envelope.Params)),
                _ => throw new ArgumentException($"Unknown bridge method '{envelope.Method}'.")
            };

            return BridgeJsonSerializer.SerializeResponse(envelope.Id, response);
        }
        catch (Exception ex)
        {
            return BridgeJsonSerializer.SerializeError(envelope.Id, "mock_error", ex.Message);
        }
    }

    private GhNewDocumentResponse HandleNewDocument(GhNewDocumentRequest request)
    {
        components.Clear();
        connections.Clear();
        activeDocumentId = $"doc_{nextDocumentId++:000}";
        activeDocumentName = string.IsNullOrWhiteSpace(request.Name) ? "Smoke Harness" : request.Name;

        return new GhNewDocumentResponse(activeDocumentId, activeDocumentName, true);
    }

    private GhAddComponentResponse HandleAddComponent(GhAddComponentRequest request)
    {
        EnsureDocumentCreated();

        var definition = allowedComponents.GetRequired(request.ComponentKey);
        var componentId = $"cmp_{nextComponentId++:000}";
        components[componentId] = new MockComponent(componentId, request.ComponentKey, definition.DisplayName, request.X, request.Y, null);

        return new GhAddComponentResponse(
            componentId,
            request.ComponentKey,
            componentId,
            definition.DisplayName,
            request.X,
            request.Y,
            definition.Inputs.Select(port => port.Name).ToArray(),
            definition.Outputs.Select(port => port.Name).ToArray());
    }

    private GhConfigureComponentResponse HandleConfigureComponent(GhConfigureComponentRequest request)
    {
        var component = GetComponent(request.ComponentId);
        components[request.ComponentId] = component with { Config = request.Config };
        return new GhConfigureComponentResponse(request.ComponentId, true, request.Config, []);
    }

    private GhConnectResponse HandleConnect(GhConnectRequest request)
    {
        _ = GetComponent(request.SourceId);
        _ = GetComponent(request.TargetId);

        var connection = new GhDocumentConnectionSnapshot(request.SourceId, request.SourceOutput, request.TargetId, request.TargetInput);
        if (!connections.Contains(connection))
        {
            connections.Add(connection);
        }

        return new GhConnectResponse(true, $"{request.SourceId}:{request.SourceOutput}->{request.TargetId}:{request.TargetInput}");
    }

    private GhDeleteComponentResponse HandleDeleteComponent(GhDeleteComponentRequest request)
    {
        var removed = connections.RemoveAll(connection =>
            string.Equals(connection.SourceId, request.ComponentId, StringComparison.Ordinal)
            || string.Equals(connection.TargetId, request.ComponentId, StringComparison.Ordinal));

        var deleted = components.Remove(request.ComponentId);
        return new GhDeleteComponentResponse(deleted, removed);
    }

    private GhInspectDocumentResponse HandleInspectDocument(GhInspectDocumentRequest request)
    {
        EnsureDocumentCreated();

        var snapshots = components.Values
            .OrderBy(component => component.X)
            .ThenBy(component => component.Y)
            .Select(component => new GhDocumentComponentSnapshot(
                component.ComponentId,
                component.ComponentKey,
                component.DisplayName,
                component.X,
                component.Y))
            .ToArray();

        var runtimeMessages = request.IncludeRuntimeMessages
            ? Array.Empty<GhRuntimeMessage>()
            : Array.Empty<GhRuntimeMessage>();

        var bounds = components.Count == 0
            ? null
            : new GhBounds(0, 0, 0, 10, 10, 10);

        return new GhInspectDocumentResponse(
            activeDocumentId!,
            snapshots,
            request.IncludeConnections ? connections.ToArray() : Array.Empty<GhDocumentConnectionSnapshot>(),
            runtimeMessages,
            bounds,
            new GhPreviewSummary(HasGeometry: components.Count > 0, PreviewObjectCount: components.Count));
    }

    private GhCapturePreviewResponse HandleCapturePreview(GhCapturePreviewRequest request)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(request.Path) ?? throw new ArgumentException("Capture path must include a directory."));
        File.WriteAllBytes(request.Path, PreviewPngBytes);
        return new GhCapturePreviewResponse(true, request.Path, request.Width, request.Height);
    }

    private GhSaveDocumentResponse HandleSaveDocument(GhSaveDocumentRequest request)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(request.Path) ?? throw new ArgumentException("Save path must include a directory."));
        File.WriteAllText(request.Path, "mock grasshopper document");
        return new GhSaveDocumentResponse(true, request.Path, "gh");
    }

    private void EnsureDocumentCreated()
    {
        if (activeDocumentId is null)
        {
            throw new InvalidOperationException("Call gh_new_document before mutating the mock bridge.");
        }
    }

    private MockComponent GetComponent(string componentId) =>
        components.TryGetValue(componentId, out var component)
            ? component
            : throw new KeyNotFoundException($"Mock component '{componentId}' was not found.");

    private static async Task<string?> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var payload = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(
                    result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                    result.CloseStatusDescription,
                    cancellationToken).ConfigureAwait(false);
                return null;
            }

            payload.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(payload.ToArray());
    }

    private static Task SendMessageAsync(WebSocket socket, string payload, CancellationToken cancellationToken)
    {
        var buffer = Encoding.UTF8.GetBytes(payload);
        return socket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed record MockComponent(
        string ComponentId,
        string ComponentKey,
        string DisplayName,
        double X,
        double Y,
        GhComponentConfig? Config);
}
