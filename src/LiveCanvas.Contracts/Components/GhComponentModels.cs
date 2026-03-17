namespace LiveCanvas.Contracts.Components;

public sealed record AllowedComponentPortInfo(
    string Name,
    string Kind,
    int Index);

public sealed record AllowedComponentConfigField(
    string Name,
    string Type,
    bool Required);

public sealed record AllowedComponentDefinition(
    string ComponentKey,
    string DisplayName,
    string Category,
    IReadOnlyList<AllowedComponentPortInfo> Inputs,
    IReadOnlyList<AllowedComponentPortInfo> Outputs,
    IReadOnlyList<AllowedComponentConfigField> ConfigFields);

public sealed record GhListAllowedComponentsRequest : IToolRequest;

public sealed record GhListAllowedComponentsResponse(
    IReadOnlyList<AllowedComponentDefinition> Components);

public sealed record GhAddComponentRequest(
    string ComponentKey,
    double X,
    double Y) : IToolRequest;

public sealed record GhAddComponentResponse(
    string ComponentId,
    string ComponentKey,
    string InstanceGuid,
    string DisplayName,
    double X,
    double Y,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs);

public sealed record SliderConfig(
    double? Min = null,
    double? Max = null,
    double? Value = null,
    bool? Integer = null);

public sealed record PanelConfig(
    string? Text = null,
    bool? Multiline = null);

public sealed record ColourSwatchConfig(
    int? R = null,
    int? G = null,
    int? B = null,
    int? A = null);

public sealed record GhComponentConfig(
    string? Nickname = null,
    SliderConfig? Slider = null,
    PanelConfig? Panel = null,
    ColourSwatchConfig? Colour = null);

public sealed record GhConfigureComponentRequest(
    string ComponentId,
    GhComponentConfig Config) : IToolRequest;

public sealed record GhConfigureComponentResponse(
    string ComponentId,
    bool Applied,
    GhComponentConfig NormalizedConfig,
    IReadOnlyList<string> Warnings);

public sealed record GhConnectRequest(
    string SourceId,
    string SourceOutput,
    string TargetId,
    string TargetInput) : IToolRequest;

public sealed record GhConnectResponse(
    bool Connected,
    string ConnectionId);

public sealed record GhDeleteComponentRequest(string ComponentId) : IToolRequest;

public sealed record GhDeleteComponentResponse(
    bool Deleted,
    int RemovedConnections);

public sealed record GhRuntimeMessage(
    string? ComponentId,
    string Level,
    string Text);

public sealed record GhSolveRequest(bool ExpireAll = true) : IToolRequest;

public sealed record GhSolveResponse(
    bool Solved,
    string Status,
    int ObjectCount,
    int WarningCount,
    int ErrorCount,
    IReadOnlyList<GhRuntimeMessage> Messages);

public sealed record GhDocumentComponentSnapshot(
    string ComponentId,
    string ComponentKey,
    string DisplayName,
    double X,
    double Y);

public sealed record GhDocumentConnectionSnapshot(
    string SourceId,
    string SourceOutput,
    string TargetId,
    string TargetInput);

public sealed record GhBounds(
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ);

public sealed record GhPreviewSummary(
    bool HasGeometry,
    int PreviewObjectCount);

public sealed record GhInspectDocumentRequest(
    bool IncludeConnections = true,
    bool IncludeRuntimeMessages = true) : IToolRequest;

public sealed record GhInspectDocumentResponse(
    string DocumentId,
    IReadOnlyList<GhDocumentComponentSnapshot> Components,
    IReadOnlyList<GhDocumentConnectionSnapshot> Connections,
    IReadOnlyList<GhRuntimeMessage> RuntimeMessages,
    GhBounds? BoundingBox,
    GhPreviewSummary PreviewSummary);
