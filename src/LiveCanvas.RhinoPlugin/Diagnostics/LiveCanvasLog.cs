namespace LiveCanvas.RhinoPlugin.Diagnostics;

internal static class LiveCanvasLog
{
    private static readonly object Sync = new();
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "livecanvas-rhino-plugin.log");

    public static string PathOnDisk => LogPath;

    public static void Write(string message)
    {
        try
        {
            var line = $"{DateTimeOffset.UtcNow:O} [t{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";
            lock (Sync)
            {
                File.AppendAllText(LogPath, line);
            }
        }
        catch
        {
            // Diagnostics must never interfere with Rhino execution.
        }
    }

    public static void Clear()
    {
        try
        {
            lock (Sync)
            {
                if (File.Exists(LogPath))
                {
                    File.Delete(LogPath);
                }
            }
        }
        catch
        {
            // Diagnostics must never interfere with Rhino execution.
        }
    }
}
