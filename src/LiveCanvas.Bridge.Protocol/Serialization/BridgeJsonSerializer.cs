using System.Text.Json;
using LiveCanvas.Bridge.Protocol.Requests;
using LiveCanvas.Bridge.Protocol.Responses;

namespace LiveCanvas.Bridge.Protocol.Serialization;

public static class BridgeJsonSerializer
{
    public static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string SerializeRequest<T>(string id, string method, T payload)
    {
        var json = JsonSerializer.SerializeToElement(payload, DefaultOptions);
        return JsonSerializer.Serialize(new JsonRpcRequestEnvelope("2.0", id, method, json), DefaultOptions);
    }

    public static JsonRpcRequestEnvelope DeserializeRequest(string json) =>
        JsonSerializer.Deserialize<JsonRpcRequestEnvelope>(json, DefaultOptions)
        ?? throw new InvalidOperationException("Failed to deserialize request envelope.");

    public static T DeserializeParams<T>(JsonElement payload) =>
        payload.Deserialize<T>(DefaultOptions)
        ?? throw new InvalidOperationException($"Failed to deserialize request payload as {typeof(T).Name}.");

    public static string SerializeResponse<T>(string id, T payload)
    {
        var result = JsonSerializer.SerializeToElement(payload, DefaultOptions);
        return JsonSerializer.Serialize(new JsonRpcResponseEnvelope("2.0", id, result, null), DefaultOptions);
    }

    public static string SerializeResponse(string id, object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var result = JsonSerializer.SerializeToElement(payload, payload.GetType(), DefaultOptions);
        return JsonSerializer.Serialize(new JsonRpcResponseEnvelope("2.0", id, result, null), DefaultOptions);
    }

    public static string SerializeError(string id, string code, string message) =>
        JsonSerializer.Serialize(new JsonRpcResponseEnvelope("2.0", id, null, new BridgeError(code, message)), DefaultOptions);

    public static JsonRpcResponseEnvelope DeserializeResponse(string json) =>
        JsonSerializer.Deserialize<JsonRpcResponseEnvelope>(json, DefaultOptions)
        ?? throw new InvalidOperationException("Failed to deserialize response envelope.");

    public static T DeserializeResult<T>(JsonRpcResponseEnvelope envelope)
    {
        if (envelope.Error is not null)
        {
            throw new InvalidOperationException($"{envelope.Error.Code}: {envelope.Error.Message}");
        }

        if (envelope.Result is null)
        {
            throw new InvalidOperationException("Bridge response did not contain a result payload.");
        }

        return envelope.Result.Value.Deserialize<T>(DefaultOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize bridge result as {typeof(T).Name}.");
    }
}
