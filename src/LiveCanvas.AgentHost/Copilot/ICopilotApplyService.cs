using LiveCanvas.Contracts.Copilot;

namespace LiveCanvas.AgentHost.Copilot;

public interface ICopilotApplyService
{
    Task<CopilotApplyPlanResponse> ApplyPlanAsync(CopilotApplyPlanRequest request, CancellationToken cancellationToken = default);
}
