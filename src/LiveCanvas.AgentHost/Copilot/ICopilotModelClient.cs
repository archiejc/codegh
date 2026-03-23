namespace LiveCanvas.AgentHost.Copilot;

public interface ICopilotModelClient
{
    Task<string> CreateReferenceBriefJsonAsync(
        string prompt,
        IReadOnlyList<string> imageDataUrls,
        CancellationToken cancellationToken = default);
}
