using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LiveCanvas.SmokeHarness;

public sealed class MockCopilotProviderServer : IAsyncDisposable
{
    private readonly HttpListener listener;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task acceptLoop;

    private MockCopilotProviderServer(HttpListener listener, int port)
    {
        this.listener = listener;
        Port = port;
        BaseUrl = $"http://127.0.0.1:{port}";
        acceptLoop = Task.Run(() => AcceptLoopAsync(shutdown.Token));
    }

    public int Port { get; }

    public string BaseUrl { get; }

    public static Task<MockCopilotProviderServer> StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var port = GetAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        return Task.FromResult(new MockCopilotProviderServer(listener, port));
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

            _ = Task.Run(() => HandleContextAsync(context), cancellationToken);
        }
    }

    private static async Task HandleContextAsync(HttpListenerContext context)
    {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(context.Request.Url?.AbsolutePath, "/chat/completions", StringComparison.Ordinal))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.Close();
            return;
        }

        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        _ = await reader.ReadToEndAsync().ConfigureAwait(false);

        var payload =
            """
            {
              "id": "mock-copilot-completion",
              "object": "chat.completion",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "{\"buildingType\":\"tower\",\"siteContext\":\"urban waterfront\",\"massingStrategy\":\"podium_tower\",\"approxDimensions\":{\"width\":48,\"depth\":34,\"height\":140},\"leveling\":{\"podiumHeight\":28,\"towerHeight\":112,\"stepCount\":3},\"transformHints\":{\"rotationDegrees\":15,\"taperRatio\":0.72,\"offsetPattern\":\"stacked\"},\"styleHints\":{\"color\":[255,255,255],\"silhouette\":\"clean\"},\"confidence\":0.93,\"assumptions\":[\"metric units\",\"massing only\"]}"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        var bytes = Encoding.UTF8.GetBytes(payload);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/json";
        context.Response.ContentEncoding = Encoding.UTF8;
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
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
}
