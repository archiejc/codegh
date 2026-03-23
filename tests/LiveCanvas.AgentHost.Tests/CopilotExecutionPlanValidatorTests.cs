using FluentAssertions;
using LiveCanvas.AgentHost.Copilot;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Planner;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.AgentHost.Tests;

public class CopilotExecutionPlanValidatorTests
{
    private readonly AllowedComponentRegistry registry = new();

    [Fact]
    public void validate_and_normalize_graph_allows_dynamic_component_keys_when_host_registry_has_not_seen_them_yet()
    {
        var validator = new CopilotExecutionPlanValidator(
            registry,
            new ComponentConfigValidator(registry),
            new ConnectionValidator(registry));

        var graph = new TemplateGraphPlan(
            "custom",
            [
                new GraphComponentPlan("source", "gh_guid:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0, 0, new GhComponentConfig()),
                new GraphComponentPlan("target", "gh_guid:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 120, 0, new GhComponentConfig())
            ],
            [
                new GraphConnectionPlan("source", "out0", "target", "in0")
            ]);

        var normalized = validator.ValidateAndNormalizeGraph(graph);

        normalized.Should().BeEquivalentTo(graph);
    }
}
