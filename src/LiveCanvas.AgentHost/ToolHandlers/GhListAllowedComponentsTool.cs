using LiveCanvas.Contracts.Components;
using LiveCanvas.Core.AllowedComponents;

namespace LiveCanvas.AgentHost.ToolHandlers;

public sealed class GhListAllowedComponentsTool
{
    private readonly AllowedComponentRegistry allowedComponentRegistry;

    public GhListAllowedComponentsTool(AllowedComponentRegistry allowedComponentRegistry)
    {
        this.allowedComponentRegistry = allowedComponentRegistry;
    }

    public Task<GhListAllowedComponentsResponse> HandleAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new GhListAllowedComponentsResponse(allowedComponentRegistry.All()));
}
