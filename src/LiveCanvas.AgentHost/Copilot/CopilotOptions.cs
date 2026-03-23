using LiveCanvas.AgentHost.Mcp;

namespace LiveCanvas.AgentHost.Copilot;

public sealed record CopilotOptions(
    string? BaseUrl,
    string? ApiKey,
    string? Model)
{
    public static CopilotOptions FromEnvironment() =>
        new(
            Environment.GetEnvironmentVariable("LIVECANVAS_COPILOT_BASE_URL"),
            Environment.GetEnvironmentVariable("LIVECANVAS_COPILOT_API_KEY"),
            Environment.GetEnvironmentVariable("LIVECANVAS_COPILOT_MODEL"));

    public void EnsureConfigured(string toolName)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            missing.Add("LIVECANVAS_COPILOT_BASE_URL");
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            missing.Add("LIVECANVAS_COPILOT_API_KEY");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            missing.Add("LIVECANVAS_COPILOT_MODEL");
        }

        if (missing.Count > 0)
        {
            throw new McpToolUnavailableException(
                toolName,
                $"Tool '{toolName}' is unavailable: missing required environment variables {string.Join(", ", missing)}.");
        }
    }
}
