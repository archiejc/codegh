using LiveCanvas.Contracts.Copilot;
using LiveCanvas.Contracts.Planner;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.AgentHost.Copilot;

public sealed class CopilotExecutionPlanValidator(
    AllowedComponentRegistry registry,
    ComponentConfigValidator componentConfigValidator,
    ConnectionValidator connectionValidator)
{
    public void EnsureCompatible(CopilotExecutionPlan executionPlan)
    {
        if (!string.Equals(executionPlan.SchemaVersion, "copilot_execution_plan/v1", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unsupported execution plan schema_version '{executionPlan.SchemaVersion}'.");
        }

        if (!string.Equals(executionPlan.TemplateName, executionPlan.GraphPlan.TemplateName, StringComparison.Ordinal))
        {
            throw new ArgumentException("execution_plan.template_name must match execution_plan.graph_plan.template_name.");
        }
    }

    public TemplateGraphPlan ValidateAndNormalizeGraph(TemplateGraphPlan graphPlan)
    {
        var normalizedComponents = new List<GraphComponentPlan>(graphPlan.Components.Count);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        var keysByAlias = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var component in graphPlan.Components)
        {
            if (string.IsNullOrWhiteSpace(component.Alias))
            {
                throw new CopilotGraphValidationException("invalid_connection", "Graph components must have non-empty aliases.");
            }

            if (!aliases.Add(component.Alias))
            {
                throw new CopilotGraphValidationException("invalid_connection", $"Duplicate graph component alias '{component.Alias}'.");
            }

            try
            {
                var normalizedConfig = registry.TryGet(component.ComponentKey, out _)
                    ? componentConfigValidator.ValidateAndNormalize(component.ComponentKey, component.Config)
                    : component.Config;
                normalizedComponents.Add(component with { Config = normalizedConfig });
                keysByAlias[component.Alias] = component.ComponentKey;
            }
            catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException)
            {
                throw new CopilotGraphValidationException(
                    "invalid_connection",
                    $"Component alias '{component.Alias}' has invalid configuration or unsupported key '{component.ComponentKey}'.",
                    ex);
            }
        }

        foreach (var connection in graphPlan.Connections)
        {
            if (!keysByAlias.TryGetValue(connection.SourceAlias, out var sourceKey))
            {
                throw new CopilotGraphValidationException("invalid_connection", $"Connection source alias '{connection.SourceAlias}' does not exist.");
            }

            if (!keysByAlias.TryGetValue(connection.TargetAlias, out var targetKey))
            {
                throw new CopilotGraphValidationException("invalid_connection", $"Connection target alias '{connection.TargetAlias}' does not exist.");
            }

            if (connectionValidator.CanValidate(sourceKey, targetKey)
                && !connectionValidator.IsValid(sourceKey, connection.SourceOutput, targetKey, connection.TargetInput))
            {
                throw new CopilotGraphValidationException(
                    "invalid_connection",
                    $"Connection '{connection.SourceAlias}.{connection.SourceOutput} -> {connection.TargetAlias}.{connection.TargetInput}' is invalid for the component ports.");
            }
        }

        return graphPlan with { Components = normalizedComponents };
    }
}

internal sealed class CopilotGraphValidationException(string failureKind, string message, Exception? innerException = null)
    : ArgumentException(message, innerException)
{
    public string FailureKind { get; } = failureKind;
}
