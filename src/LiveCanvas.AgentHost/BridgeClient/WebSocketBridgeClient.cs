using System.Net.WebSockets;
using System.Text;
using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Bridge.Protocol.Serialization;

namespace LiveCanvas.AgentHost.BridgeClient;

public sealed class WebSocketBridgeClient : IBridgeClient
{
    private readonly Uri bridgeUri;

    public WebSocketBridgeClient(string? bridgeUri = null)
    {
        this.bridgeUri = new Uri(bridgeUri ?? Environment.GetEnvironmentVariable("LIVECANVAS_BRIDGE_URI") ?? BridgeDefaults.WebSocketUri);
    }

    public async Task<TResponse> InvokeAsync<TResponse>(string method, object? payload, CancellationToken cancellationToken = default)
    {
        using var socket = new ClientWebSocket();

        try
        {
            await socket.ConnectAsync(bridgeUri, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new BridgeClientUnavailableException($"Could not connect to the LiveCanvas Rhino bridge at '{bridgeUri}'. Start Rhino 8 with the LiveCanvas plugin loaded and try again. {ex.Message}");
        }

        var requestJson = BridgeJsonSerializer.SerializeRequest(Guid.NewGuid().ToString("N"), method, payload ?? new { });
        await SendAsync(socket, requestJson, cancellationToken);
        var responseJson = await ReceiveAsync(socket, cancellationToken);
        var envelope = BridgeJsonSerializer.DeserializeResponse(responseJson);
        return BridgeJsonSerializer.DeserializeResult<TResponse>(envelope);
    }

    private static Task SendAsync(ClientWebSocket socket, string payload, CancellationToken cancellationToken)
    {
        var buffer = Encoding.UTF8.GetBytes(payload);
        return socket.SendAsync(
            new ArraySegment<byte>(buffer),
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }

    private static async Task<string> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new ArraySegment<byte>(new byte[8192]);
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new BridgeClientUnavailableException("The LiveCanvas Rhino bridge closed the connection unexpectedly.");
            }

            stream.Write(buffer.Array!, buffer.Offset, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
