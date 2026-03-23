using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Validation;
using LiveCanvas.RhinoPlugin.Diagnostics;
using LiveCanvas.RhinoPlugin.Bridge;
using LiveCanvas.RhinoPlugin.Runtime;
using Rhino.PlugIns;

namespace LiveCanvas.RhinoPlugin;

public sealed class LiveCanvasPlugin : PlugIn
{
    private LiveCanvasBridgeServer? bridgeServer;

    public static LiveCanvasPlugin? Instance { get; private set; }

    public LiveCanvasPlugin()
    {
        Instance = this;
    }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        try
        {
            LiveCanvasLog.Clear();
            LiveCanvasLog.Write("plugin onload start");
            var registry = new AllowedComponentRegistry();
            var runtime = new LiveCanvasRuntime(
                registry,
                new ComponentConfigValidator(registry),
                new ComponentConfigV2Validator(registry),
                new ConnectionValidator(registry));

            bridgeServer = new LiveCanvasBridgeServer(
                new RhinoUiDispatcher(),
                new LiveCanvasBridgeDispatcher(runtime));

            bridgeServer.Start();
            LiveCanvasLog.Write("plugin onload completed");
            return LoadReturnCode.Success;
        }
        catch (Exception ex)
        {
            LiveCanvasLog.Write($"plugin onload failed: {ex}");
            errorMessage = ex.Message;
            return LoadReturnCode.ErrorShowDialog;
        }
    }

    protected override void OnShutdown()
    {
        LiveCanvasLog.Write("plugin shutdown");
        bridgeServer?.Dispose();
        bridgeServer = null;
        base.OnShutdown();
    }
}
