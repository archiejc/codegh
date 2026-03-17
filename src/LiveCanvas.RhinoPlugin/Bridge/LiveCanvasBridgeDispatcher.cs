using LiveCanvas.Bridge.Protocol;
using LiveCanvas.Bridge.Protocol.Requests;
using LiveCanvas.Bridge.Protocol.Serialization;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Contracts.Session;
using LiveCanvas.RhinoPlugin.Runtime;

namespace LiveCanvas.RhinoPlugin.Bridge;

public sealed class LiveCanvasBridgeDispatcher
{
    private readonly LiveCanvasRuntime runtime;

    public LiveCanvasBridgeDispatcher(LiveCanvasRuntime runtime)
    {
        this.runtime = runtime;
    }

    public string Dispatch(string requestJson)
    {
        JsonRpcRequestEnvelope? envelope = null;

        try
        {
            envelope = BridgeJsonSerializer.DeserializeRequest(requestJson);
            object response = envelope.Method switch
            {
                BridgeMethodNames.GhSessionInfo => runtime.GetSessionInfo(),
                BridgeMethodNames.GhNewDocument => runtime.NewDocument(BridgeJsonSerializer.DeserializeParams<GhNewDocumentRequest>(envelope.Params)),
                BridgeMethodNames.GhListAllowedComponents => runtime.ListAllowedComponents(),
                BridgeMethodNames.GhAddComponent => runtime.AddComponent(BridgeJsonSerializer.DeserializeParams<GhAddComponentRequest>(envelope.Params)),
                BridgeMethodNames.GhConfigureComponent => runtime.ConfigureComponent(BridgeJsonSerializer.DeserializeParams<GhConfigureComponentRequest>(envelope.Params)),
                BridgeMethodNames.GhConnect => runtime.Connect(BridgeJsonSerializer.DeserializeParams<GhConnectRequest>(envelope.Params)),
                BridgeMethodNames.GhDeleteComponent => runtime.DeleteComponent(BridgeJsonSerializer.DeserializeParams<GhDeleteComponentRequest>(envelope.Params)),
                BridgeMethodNames.GhSolve => runtime.Solve(BridgeJsonSerializer.DeserializeParams<GhSolveRequest>(envelope.Params)),
                BridgeMethodNames.GhInspectDocument => runtime.InspectDocument(BridgeJsonSerializer.DeserializeParams<GhInspectDocumentRequest>(envelope.Params)),
                BridgeMethodNames.GhCapturePreview => runtime.CapturePreview(BridgeJsonSerializer.DeserializeParams<GhCapturePreviewRequest>(envelope.Params)),
                BridgeMethodNames.GhSaveDocument => runtime.SaveDocument(BridgeJsonSerializer.DeserializeParams<GhSaveDocumentRequest>(envelope.Params)),
                _ => throw new ArgumentException($"Unknown bridge method '{envelope.Method}'.")
            };

            return BridgeJsonSerializer.SerializeResponse(envelope.Id, response);
        }
        catch (Exception ex)
        {
            var requestId = envelope?.Id ?? string.Empty;
            return BridgeJsonSerializer.SerializeError(requestId, MapErrorCode(ex), ex.Message);
        }
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
