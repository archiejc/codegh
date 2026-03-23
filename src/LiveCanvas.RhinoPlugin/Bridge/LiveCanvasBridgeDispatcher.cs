using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Bridge.Protocol.Requests;
using LiveCanvas.Bridge.Protocol.Serialization;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Contracts.Session;
using LiveCanvas.RhinoPlugin.Diagnostics;
using LiveCanvas.RhinoPlugin.Runtime;

namespace LiveCanvas.RhinoPlugin.Bridge;

public sealed class LiveCanvasBridgeDispatcher
{
    private readonly LiveCanvasRuntime runtime;

    public LiveCanvasBridgeDispatcher(LiveCanvasRuntime runtime)
    {
        this.runtime = runtime;
    }

    public JsonRpcRequestEnvelope DeserializeRequest(string requestJson)
    {
        LiveCanvasLog.Write("bridge dispatcher deserialize start");
        var envelope = BridgeJsonSerializer.DeserializeRequest(requestJson);
        LiveCanvasLog.Write($"bridge dispatcher deserialized method={envelope.Method} id={envelope.Id}");
        return envelope;
    }

    public object Dispatch(JsonRpcRequestEnvelope envelope)
    {
        LiveCanvasLog.Write($"bridge dispatcher handling method={envelope.Method} id={envelope.Id}");
        object response = envelope.Method switch
        {
            BridgeMethodNames.GhSessionInfo => runtime.GetSessionInfo(),
            BridgeMethodNames.GhNewDocument => runtime.NewDocument(BridgeJsonSerializer.DeserializeParams<GhNewDocumentRequest>(envelope.Params)),
            BridgeMethodNames.GhListAllowedComponents => runtime.ListAllowedComponents(),
            BridgeMethodNames.GhAddComponent => runtime.AddComponent(BridgeJsonSerializer.DeserializeParams<GhAddComponentRequest>(envelope.Params)),
            BridgeMethodNames.GhConfigureComponent => runtime.ConfigureComponent(BridgeJsonSerializer.DeserializeParams<GhConfigureComponentRequest>(envelope.Params)),
            BridgeMethodNames.GhConfigureComponentV2 => runtime.ConfigureComponentV2(BridgeJsonSerializer.DeserializeParams<GhConfigureComponentV2Request>(envelope.Params)),
            BridgeMethodNames.GhConnect => runtime.Connect(BridgeJsonSerializer.DeserializeParams<GhConnectRequest>(envelope.Params)),
            BridgeMethodNames.GhDeleteComponent => runtime.DeleteComponent(BridgeJsonSerializer.DeserializeParams<GhDeleteComponentRequest>(envelope.Params)),
            BridgeMethodNames.GhSolve => runtime.Solve(BridgeJsonSerializer.DeserializeParams<GhSolveRequest>(envelope.Params)),
            BridgeMethodNames.GhInspectDocument => runtime.InspectDocument(BridgeJsonSerializer.DeserializeParams<GhInspectDocumentRequest>(envelope.Params)),
            BridgeMethodNames.GhCapturePreview => runtime.CapturePreview(BridgeJsonSerializer.DeserializeParams<GhCapturePreviewRequest>(envelope.Params)),
            BridgeMethodNames.GhSaveDocument => runtime.SaveDocument(BridgeJsonSerializer.DeserializeParams<GhSaveDocumentRequest>(envelope.Params)),
            _ => throw new ArgumentException($"Unknown bridge method '{envelope.Method}'.")
        };

        LiveCanvasLog.Write($"bridge dispatcher completed method={envelope.Method}");
        return response;
    }

    public string SerializeResponse(string requestId, object response) =>
        BridgeJsonSerializer.SerializeResponse(requestId, response);

    public string SerializeError(string? requestId, Exception exception)
    {
        LiveCanvasLog.Write($"bridge dispatcher failed: {exception}");
        return BridgeJsonSerializer.SerializeError(requestId ?? string.Empty, MapErrorCode(exception), exception.Message);
    }

    private static string MapErrorCode(Exception exception) =>
        exception switch
        {
            ArgumentException => "invalid_argument",
            KeyNotFoundException => "not_found",
            InvalidOperationException => "invalid_operation",
            _ => "runtime_failure"
        };
}
