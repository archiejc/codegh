using System.Text.Json.Serialization;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Contracts.Planner;
using LiveCanvas.Contracts.ReferenceInterpretation;

namespace LiveCanvas.Contracts.Copilot;

public sealed record CopilotExecutionPlan(
    [property: JsonPropertyName("input_prompt")] string InputPrompt,
    [property: JsonPropertyName("input_images")] IReadOnlyList<string> InputImages,
    [property: JsonPropertyName("reference_brief")] ReferenceBrief ReferenceBrief,
    [property: JsonPropertyName("template_name")] string TemplateName,
    [property: JsonPropertyName("graph_plan")] TemplateGraphPlan GraphPlan,
    [property: JsonPropertyName("assumptions")] IReadOnlyList<string> Assumptions,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("suggested_document_name")] string SuggestedDocumentName)
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "copilot_execution_plan/v1";
}

public sealed record CopilotPlanRequest(
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("image_paths")] IReadOnlyList<string>? ImagePaths = null);

public sealed record CopilotPlanResponse(
    [property: JsonPropertyName("execution_plan")] CopilotExecutionPlan ExecutionPlan);

public sealed record CopilotApplyPlanRequest(
    [property: JsonPropertyName("execution_plan")] CopilotExecutionPlan ExecutionPlan,
    [property: JsonPropertyName("output_dir")] string? OutputDir = null,
    [property: JsonPropertyName("preview_width")] int? PreviewWidth = null,
    [property: JsonPropertyName("preview_height")] int? PreviewHeight = null,
    [property: JsonPropertyName("expire_all")] bool? ExpireAll = null);

public sealed record CopilotApplyPlanResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("repair_iterations")] int RepairIterations,
    [property: JsonPropertyName("repair_actions")] IReadOnlyList<string> RepairActions,
    [property: JsonPropertyName("new_document")] GhNewDocumentResponse? NewDocument,
    [property: JsonPropertyName("solve")] GhSolveResponse? Solve,
    [property: JsonPropertyName("inspect")] GhInspectDocumentResponse? Inspect,
    [property: JsonPropertyName("preview_path")] string? PreviewPath,
    [property: JsonPropertyName("document_path")] string? DocumentPath,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);
