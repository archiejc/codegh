namespace LiveCanvas.AgentHost.ToolHandlers;

internal static class ToolPathValidator
{
    private static readonly string[] PreviewExtensions = [".png", ".jpg", ".jpeg"];

    public static void RequireAbsoluteGhPath(string path, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            throw new ArgumentException("Path must be absolute.", argumentName);
        }

        if (!string.Equals(Path.GetExtension(path), ".gh", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only .gh files are allowed in v0.", argumentName);
        }
    }

    public static void RequireAbsolutePreviewPath(string path, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            throw new ArgumentException("Path must be absolute.", argumentName);
        }

        var extension = Path.GetExtension(path);
        if (!PreviewExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Preview captures must be written as .png, .jpg, or .jpeg.", argumentName);
        }
    }
}
