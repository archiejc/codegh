using System.Text.Json;
using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.Copilot;
using LiveCanvas.AgentHost.ToolHandlers;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Copilot;

namespace LiveCanvas.AgentHost.Mcp;

internal sealed class McpToolExecutor
{
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GhSessionInfoTool sessionInfoTool;
    private readonly GhNewDocumentTool newDocumentTool;
    private readonly GhListAllowedComponentsTool listAllowedComponentsTool;
    private readonly GhAddComponentTool addComponentTool;
    private readonly GhConfigureComponentTool configureComponentTool;
    private readonly GhConnectTool connectTool;
    private readonly GhDeleteComponentTool deleteComponentTool;
    private readonly GhSolveTool solveTool;
    private readonly GhInspectDocumentTool inspectDocumentTool;
    private readonly GhCapturePreviewTool capturePreviewTool;
    private readonly GhSaveDocumentTool saveDocumentTool;
    private readonly ICopilotPlanService copilotPlanService;
    private readonly ICopilotApplyService copilotApplyService;

    public McpToolExecutor(
        GhSessionInfoTool sessionInfoTool,
        GhNewDocumentTool newDocumentTool,
        GhListAllowedComponentsTool listAllowedComponentsTool,
        GhAddComponentTool addComponentTool,
        GhConfigureComponentTool configureComponentTool,
        GhConnectTool connectTool,
        GhDeleteComponentTool deleteComponentTool,
        GhSolveTool solveTool,
        GhInspectDocumentTool inspectDocumentTool,
        GhCapturePreviewTool capturePreviewTool,
        GhSaveDocumentTool saveDocumentTool,
        ICopilotPlanService copilotPlanService,
        ICopilotApplyService copilotApplyService)
    {
        this.sessionInfoTool = sessionInfoTool;
        this.newDocumentTool = newDocumentTool;
        this.listAllowedComponentsTool = listAllowedComponentsTool;
        this.addComponentTool = addComponentTool;
        this.configureComponentTool = configureComponentTool;
        this.connectTool = connectTool;
        this.deleteComponentTool = deleteComponentTool;
        this.solveTool = solveTool;
        this.inspectDocumentTool = inspectDocumentTool;
        this.capturePreviewTool = capturePreviewTool;
        this.saveDocumentTool = saveDocumentTool;
        this.copilotPlanService = copilotPlanService;
        this.copilotApplyService = copilotApplyService;
    }

    public async Task<object> InvokeAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        return toolName switch
        {
            ToolDefinitions.GhSessionInfo => await sessionInfoTool.HandleAsync(cancellationToken),
            ToolDefinitions.GhNewDocument => await newDocumentTool.HandleAsync(GetOptionalString(arguments, "name"), cancellationToken),
            ToolDefinitions.GhListAllowedComponents => await listAllowedComponentsTool.HandleAsync(cancellationToken),
            ToolDefinitions.GhAddComponent => await addComponentTool.HandleAsync(
                GetRequiredString(arguments, "component_key"),
                GetRequiredDouble(arguments, "x"),
                GetRequiredDouble(arguments, "y"),
                cancellationToken),
            ToolDefinitions.GhConfigureComponent => await configureComponentTool.HandleAsync(
                GetRequiredString(arguments, "component_id"),
                Deserialize<GhComponentConfig>(arguments, "config"),
                cancellationToken),
            ToolDefinitions.GhConnect => await connectTool.HandleAsync(
                GetRequiredString(arguments, "source_id"),
                GetRequiredString(arguments, "source_output"),
                GetRequiredString(arguments, "target_id"),
                GetRequiredString(arguments, "target_input"),
                cancellationToken),
            ToolDefinitions.GhDeleteComponent => await deleteComponentTool.HandleAsync(GetRequiredString(arguments, "component_id"), cancellationToken),
            ToolDefinitions.GhSolve => await solveTool.HandleAsync(GetOptionalBoolean(arguments, "expire_all") ?? true, cancellationToken),
            ToolDefinitions.GhInspectDocument => await inspectDocumentTool.HandleAsync(
                GetOptionalBoolean(arguments, "include_connections") ?? true,
                GetOptionalBoolean(arguments, "include_runtime_messages") ?? true,
                cancellationToken),
            ToolDefinitions.GhCapturePreview => await capturePreviewTool.HandleAsync(
                GetRequiredString(arguments, "path"),
                GetRequiredInt(arguments, "width"),
                GetRequiredInt(arguments, "height"),
                cancellationToken),
            ToolDefinitions.GhSaveDocument => await saveDocumentTool.HandleAsync(GetRequiredString(arguments, "path"), cancellationToken),
            ToolDefinitions.CopilotPlan => await copilotPlanService.CreatePlanAsync(ParseCopilotPlanRequest(arguments), cancellationToken),
            ToolDefinitions.CopilotApplyPlan => await copilotApplyService.ApplyPlanAsync(ParseCopilotApplyPlanRequest(arguments), cancellationToken),
            _ => throw new ArgumentException($"Unknown MCP tool '{toolName}'.")
        };
    }

    private CopilotPlanRequest ParseCopilotPlanRequest(JsonElement arguments)
    {
        var prompt = GetRequiredString(arguments, "prompt");
        IReadOnlyList<string>? imagePaths = null;

        if (arguments.TryGetProperty("image_paths", out var imagePathsElement))
        {
            imagePaths = ParseOptionalStringArray("image_paths", imagePathsElement);
        }

        return new CopilotPlanRequest(prompt, imagePaths);
    }

    private CopilotApplyPlanRequest ParseCopilotApplyPlanRequest(JsonElement arguments)
    {
        var executionPlan = Deserialize<CopilotExecutionPlan>(arguments, "execution_plan");
        var outputDir = GetOptionalString(arguments, "output_dir");
        var previewWidth = GetOptionalInt(arguments, "preview_width");
        var previewHeight = GetOptionalInt(arguments, "preview_height");
        var expireAll = GetOptionalBoolean(arguments, "expire_all");

        return new CopilotApplyPlanRequest(
            executionPlan,
            outputDir,
            previewWidth,
            previewHeight,
            expireAll);
    }

    private T DeserializeRoot<T>(JsonElement value) =>
        value.Deserialize<T>(jsonOptions)
            ?? throw new ArgumentException($"Could not deserialize request as {typeof(T).Name}.");

    private T Deserialize<T>(JsonElement arguments, string propertyName)
    {
        if (!arguments.TryGetProperty(propertyName, out var property))
        {
            throw new ArgumentException($"Missing required argument '{propertyName}'.");
        }

        try
        {
            return property.Deserialize<T>(jsonOptions)
                ?? throw new ArgumentException($"Could not deserialize '{propertyName}' as {typeof(T).Name}.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Could not deserialize '{propertyName}' as {typeof(T).Name}.", ex);
        }
    }

    private static IReadOnlyList<string>? ParseOptionalStringArray(string propertyName, JsonElement property)
    {
        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"Optional argument '{propertyName}' must be an array of strings.");
        }

        var items = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new ArgumentException($"Optional argument '{propertyName}' must be an array of non-empty strings.");
            }

            items.Add(item.GetString()!);
        }

        return items;
    }

    private static string GetRequiredString(JsonElement arguments, string propertyName) =>
        arguments.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()!
            : throw new ArgumentException($"Missing required string argument '{propertyName}'.");

    private static string? GetOptionalString(JsonElement arguments, string propertyName) =>
        arguments.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static double GetRequiredDouble(JsonElement arguments, string propertyName) =>
        arguments.TryGetProperty(propertyName, out var property) && property.TryGetDouble(out var value)
            ? value
            : throw new ArgumentException($"Missing required numeric argument '{propertyName}'.");

    private static int GetRequiredInt(JsonElement arguments, string propertyName) =>
        arguments.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : throw new ArgumentException($"Missing required integer argument '{propertyName}'.");

    private static int? GetOptionalInt(JsonElement arguments, string propertyName) =>
        arguments.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;

    private static bool? GetOptionalBoolean(JsonElement arguments, string propertyName) =>
        arguments.TryGetProperty(propertyName, out var property) && (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? property.GetBoolean()
            : null;
}
