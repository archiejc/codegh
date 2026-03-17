using System.Text.Json;

namespace LiveCanvas.Bridge.Protocol.Responses;

public sealed record BridgeError(
    string Code,
    string Message);

public sealed record JsonRpcResponseEnvelope(
    string Jsonrpc,
    string Id,
    JsonElement? Result,
    BridgeError? Error);
