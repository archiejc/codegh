using LiveCanvas.Contracts.Copilot;

namespace LiveCanvas.AgentHost.Copilot;

public interface ICopilotPlanService
{
    Task<CopilotPlanResponse> CreatePlanAsync(CopilotPlanRequest request, CancellationToken cancellationToken = default);
}
