using System.Text.Json;
using LiveCanvas.AgentHost.BridgeClient;
using LiveCanvas.AgentHost.ToolHandlers;
using LiveCanvas.AgentHost.ToolState;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Validation;

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

    public McpToolExecutor()
    {
        var registry = new AllowedComponentRegistry();
        var componentSessionState = new ComponentSessionState();
        var bridgeClient = new WebSocketBridgeClient();

        sessionInfoTool = new GhSessionInfoTool(bridgeClient);
        newDocumentTool = new GhNewDocumentTool(bridgeClient, componentSessionState);
        listAllowedComponentsTool = new GhListAllowedComponentsTool(registry);
        addComponentTool = new GhAddComponentTool(bridgeClient, registry, componentSessionState);
        configureComponentTool = new GhConfigureComponentTool(bridgeClient, componentSessionState, new ComponentConfigValidator(registry));
        connectTool = new GhConnectTool(bridgeClient, componentSessionState, new ConnectionValidator(registry));
        deleteComponentTool = new GhDeleteComponentTool(bridgeClient, componentSessionState);
        solveTool = new GhSolveTool(bridgeClient);
        inspectDocumentTool = new GhInspectDocumentTool(bridgeClient);
        capturePreviewTool = new GhCapturePreviewTool(bridgeClient);
        saveDocumentTool = new GhSaveDocumentTool(bridgeClient);
    }

    public async Task<object> InvokeAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        return toolName switch
        {
            "gh_session_info" => await sessionInfoTool.HandleAsync(cancellationToken),
            "gh_new_document" => await newDocumentTool.HandleAsync(GetOptionalString(arguments, "name"), cancellationToken),
            "gh_list_allowed_components" => await listAllowedComponentsTool.HandleAsync(cancellationToken),
            "gh_add_component" => await addComponentTool.HandleAsync(
                GetRequiredString(arguments, "component_key"),
                GetRequiredDouble(arguments, "x"),
                GetRequiredDouble(arguments, "y"),
                cancellationToken),
            "gh_configure_component" => await configureComponentTool.HandleAsync(
                GetRequiredString(arguments, "component_id"),
                Deserialize<GhComponentConfig>(arguments, "config"),
                cancellationToken),
            "gh_connect" => await connectTool.HandleAsync(
                GetRequiredString(arguments, "source_id"),
                GetRequiredString(arguments, "source_output"),
                GetRequiredString(arguments, "target_id"),
                GetRequiredString(arguments, "target_input"),
                cancellationToken),
            "gh_delete_component" => await deleteComponentTool.HandleAsync(GetRequiredString(arguments, "component_id"), cancellationToken),
            "gh_solve" => await solveTool.HandleAsync(GetOptionalBoolean(arguments, "expire_all") ?? true, cancellationToken),
            "gh_inspect_document" => await inspectDocumentTool.HandleAsync(
                GetOptionalBoolean(arguments, "include_connections") ?? true,
                GetOptionalBoolean(arguments, "include_runtime_messages") ?? true,
                cancellationToken),
            "gh_capture_preview" => await capturePreviewTool.HandleAsync(
                GetRequiredString(arguments, "path"),
                GetRequiredInt(arguments, "width"),
                GetRequiredInt(arguments, "height"),
                cancellationToken),
            "gh_save_document" => await saveDocumentTool.HandleAsync(GetRequiredString(arguments, "path"), cancellationToken),
            _ => throw new ArgumentException($"Unknown MCP tool '{toolName}'.")
        };
    }

    private T Deserialize<T>(JsonElement arguments, string propertyName)
    {
        if (!arguments.TryGetProperty(propertyName, out var property))
        {
            throw new ArgumentException($"Missing required argument '{propertyName}'.");
        }

        return property.Deserialize<T>(jsonOptions)
            ?? throw new ArgumentException($"Could not deserialize '{propertyName}' as {typeof(T).Name}.");
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

    private static bool? GetOptionalBoolean(JsonElement arguments, string propertyName) =>
        arguments.TryGetProperty(propertyName, out var property) && (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? property.GetBoolean()
            : null;
}
