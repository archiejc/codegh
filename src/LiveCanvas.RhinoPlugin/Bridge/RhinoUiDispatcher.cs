using Rhino;

namespace LiveCanvas.RhinoPlugin.Bridge;

public sealed class RhinoUiDispatcher
{
    public Task<T> InvokeAsync<T>(Func<T> callback, CancellationToken cancellationToken = default)
    {
        if (!RhinoApp.InvokeRequired)
        {
            return Task.FromResult(callback());
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(
            new Action(() =>
            {
                try
                {
                    completion.TrySetResult(callback());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }),
            Array.Empty<object>());

        return completion.Task.WaitAsync(cancellationToken);
    }
}
