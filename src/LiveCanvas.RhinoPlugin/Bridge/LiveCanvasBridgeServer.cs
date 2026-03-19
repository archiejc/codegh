using System.Net;
using System.Net.WebSockets;
using System.Text;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Bridge.Protocol.Requests;
using LiveCanvas.RhinoPlugin.Diagnostics;

namespace LiveCanvas.RhinoPlugin.Bridge;

public sealed class LiveCanvasBridgeServer : IDisposable
{
    private readonly RhinoUiDispatcher uiDispatcher;
    private readonly LiveCanvasBridgeDispatcher dispatcher;
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource shutdown = new();
    private Task? acceptLoop;

    public LiveCanvasBridgeServer(RhinoUiDispatcher uiDispatcher, LiveCanvasBridgeDispatcher dispatcher)
    {
        this.uiDispatcher = uiDispatcher;
        this.dispatcher = dispatcher;
        listener.Prefixes.Add(BridgeDefaults.HttpPrefix);
    }

    public void Start()
    {
        if (listener.IsListening)
        {
            return;
        }

        listener.Start();
        LiveCanvasLog.Write($"bridge server listening at {BridgeDefaults.HttpPrefix}");
        acceptLoop = Task.Run(() => AcceptLoopAsync(shutdown.Token), shutdown.Token);
    }

    public void Dispose()
    {
        shutdown.Cancel();

        if (listener.IsListening)
        {
            listener.Stop();
        }

        listener.Close();

        try
        {
            acceptLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore bridge shutdown races during Rhino plugin unload.
        }

        shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
                LiveCanvasLog.Write($"bridge server accepted context path={context.Request.Url?.AbsolutePath} websocket={context.Request.IsWebSocketRequest}");
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }

            _ = Task.Run(() => HandleContextAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            LiveCanvasLog.Write($"bridge server handling context path={context.Request.Url?.AbsolutePath} websocket={context.Request.IsWebSocketRequest}");
            if (!context.Request.IsWebSocketRequest || context.Request.Url?.AbsolutePath != BridgeDefaults.WebSocketPath)
            {
                LiveCanvasLog.Write("bridge server rejected non-websocket or unexpected path request");
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                return;
            }

            LiveCanvasLog.Write("bridge server accepting websocket");
            var socketContext = await context.AcceptWebSocketAsync(subProtocol: null);
            LiveCanvasLog.Write("bridge server websocket accepted");
            using var socket = socketContext.WebSocket;

            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var requestJson = await ReceiveMessageAsync(socket, cancellationToken);
                if (string.IsNullOrWhiteSpace(requestJson))
                {
                    LiveCanvasLog.Write("bridge server received empty websocket message or close");
                    break;
                }

                LiveCanvasLog.Write($"bridge server received payload={requestJson}");
                JsonRpcRequestEnvelope? request = null;
                string responseJson;

                try
                {
                    request = dispatcher.DeserializeRequest(requestJson);
                    LiveCanvasLog.Write($"bridge server dispatching method={request.Method} on ui thread");
                    var requestEnvelope = request;
                    var responsePayload = await uiDispatcher.InvokeAsync(() => dispatcher.Dispatch(requestEnvelope), cancellationToken);
                    LiveCanvasLog.Write($"bridge server ui dispatch returned method={requestEnvelope.Method}");
                    responseJson = dispatcher.SerializeResponse(requestEnvelope.Id, responsePayload);
                }
                catch (Exception ex)
                {
                    responseJson = dispatcher.SerializeError(request?.Id, ex);
                }

                LiveCanvasLog.Write($"bridge server sending payload={responseJson}");
                await SendMessageAsync(socket, responseJson, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            LiveCanvasLog.Write($"bridge server context handling failed: {ex}");
        }
    }

    private static async Task<string?> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new ArraySegment<byte>(new byte[8192]);
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", cancellationToken);
                return null;
            }

            stream.Write(buffer.Array!, buffer.Offset, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static Task SendMessageAsync(WebSocket socket, string payload, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        return socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }
}
