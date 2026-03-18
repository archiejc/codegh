using LiveCanvas.RhinoPlugin.Diagnostics;
using Rhino;

namespace LiveCanvas.RhinoPlugin.Bridge;

public sealed class RhinoUiDispatcher
{
    public Task<T> InvokeAsync<T>(Func<T> callback, CancellationToken cancellationToken = default)
    {
        LiveCanvasLog.Write($"ui-dispatch invoke requested; invokeRequired={RhinoApp.InvokeRequired}");

        if (!RhinoApp.InvokeRequired)
        {
            LiveCanvasLog.Write("ui-dispatch executing inline");
            return Task.FromResult(callback());
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(
            new Action(() =>
            {
                try
                {
                    LiveCanvasLog.Write("ui-dispatch callback entered on ui thread");
                    var result = callback();
                    LiveCanvasLog.Write("ui-dispatch callback returned from delegate");
                    completion.TrySetResult(result);
                    LiveCanvasLog.Write("ui-dispatch callback completed");
                }
                catch (Exception ex)
                {
                    LiveCanvasLog.Write($"ui-dispatch callback failed: {ex}");
                    completion.TrySetException(ex);
                }
            }),
            Array.Empty<object>());

        LiveCanvasLog.Write("ui-dispatch posted to ui thread");
        return completion.Task.WaitAsync(cancellationToken);
    }
}
