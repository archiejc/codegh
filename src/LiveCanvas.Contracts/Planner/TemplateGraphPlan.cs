using LiveCanvas.Contracts.Components;

namespace LiveCanvas.Contracts.Planner;

public sealed record GraphComponentPlan(
    string Alias,
    string ComponentKey,
    double X,
    double Y,
    GhComponentConfig Config);

public sealed record GraphConnectionPlan(
    string SourceAlias,
    string SourceOutput,
    string TargetAlias,
    string TargetInput);

public sealed record TemplateGraphPlan(
    string TemplateName,
    IReadOnlyList<GraphComponentPlan> Components,
    IReadOnlyList<GraphConnectionPlan> Connections);
