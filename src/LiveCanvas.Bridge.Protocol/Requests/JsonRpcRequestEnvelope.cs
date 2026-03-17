using System.Text.Json;

namespace LiveCanvas.Bridge.Protocol.Requests;

public sealed record JsonRpcRequestEnvelope(
    string Jsonrpc,
    string Id,
    string Method,
    JsonElement Params);
