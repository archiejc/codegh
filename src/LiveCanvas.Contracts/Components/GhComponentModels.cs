using System.Text.Json.Serialization;

namespace LiveCanvas.Contracts.Components;

public sealed record AllowedComponentPortInfo(
    string Name,
    string Kind,
    int Index);

public sealed record AllowedComponentConfigField(
    string Name,
    string Type,
    bool Required);

public sealed record AllowedComponentConfigOpDescriptor(
    string Kind,
    string? Adapter = null,
    IReadOnlyList<string>? SupportedValueTypes = null,
    IReadOnlyList<string>? SupportedFlags = null);

public sealed record AllowedComponentDefinition(
    string ComponentKey,
    string DisplayName,
    string Category,
    IReadOnlyList<AllowedComponentPortInfo> Inputs,
    IReadOnlyList<AllowedComponentPortInfo> Outputs,
    IReadOnlyList<AllowedComponentConfigField> ConfigFields,
    IReadOnlyList<AllowedComponentConfigOpDescriptor>? ConfigOps = null);

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

public sealed record GhComponentConfigV2(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("ops")] IReadOnlyList<GhComponentConfigOp> Ops);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SetNicknameComponentConfigOp), "set_nickname")]
[JsonDerivedType(typeof(SetInputPersistentDataComponentConfigOp), "set_input_persistent_data")]
[JsonDerivedType(typeof(ClearInputPersistentDataComponentConfigOp), "clear_input_persistent_data")]
[JsonDerivedType(typeof(SetParamFlagsComponentConfigOp), "set_param_flags")]
[JsonDerivedType(typeof(AdapterConfigComponentConfigOp), "adapter_config")]
public abstract record GhComponentConfigOp;

public sealed record SetNicknameComponentConfigOp(
    [property: JsonPropertyName("value")] string Value) : GhComponentConfigOp;

public sealed record SetInputPersistentDataComponentConfigOp(
    [property: JsonPropertyName("input")] string Input,
    [property: JsonPropertyName("value")] GhValue Value) : GhComponentConfigOp;

public sealed record ClearInputPersistentDataComponentConfigOp(
    [property: JsonPropertyName("input")] string Input) : GhComponentConfigOp;

public sealed record SetParamFlagsComponentConfigOp(
    [property: JsonPropertyName("param")] string Param,
    [property: JsonPropertyName("flatten")] bool? Flatten = null,
    [property: JsonPropertyName("graft")] bool? Graft = null,
    [property: JsonPropertyName("simplify")] bool? Simplify = null) : GhComponentConfigOp;

public sealed record AdapterConfigComponentConfigOp(
    [property: JsonPropertyName("config")] GhComponentAdapterConfig Config) : GhComponentConfigOp;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "adapter")]
[JsonDerivedType(typeof(NumberSliderAdapterConfig), "number_slider")]
[JsonDerivedType(typeof(PanelAdapterConfig), "panel")]
[JsonDerivedType(typeof(ColourSwatchAdapterConfig), "colour_swatch")]
[JsonDerivedType(typeof(BooleanToggleAdapterConfig), "boolean_toggle")]
[JsonDerivedType(typeof(ValueListAdapterConfig), "value_list")]
public abstract record GhComponentAdapterConfig;

public sealed record NumberSliderAdapterConfig(
    [property: JsonPropertyName("min")] double? Min = null,
    [property: JsonPropertyName("max")] double? Max = null,
    [property: JsonPropertyName("value")] double? Value = null,
    [property: JsonPropertyName("integer")] bool? Integer = null) : GhComponentAdapterConfig;

public sealed record PanelAdapterConfig(
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("multiline")] bool? Multiline = null) : GhComponentAdapterConfig;

public sealed record ColourSwatchAdapterConfig(
    [property: JsonPropertyName("r")] int? R = null,
    [property: JsonPropertyName("g")] int? G = null,
    [property: JsonPropertyName("b")] int? B = null,
    [property: JsonPropertyName("a")] int? A = null) : GhComponentAdapterConfig;

public sealed record BooleanToggleAdapterConfig(
    [property: JsonPropertyName("value")] bool? Value = null) : GhComponentAdapterConfig;

public sealed record GhValueListItemConfig(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("expression")] string Expression);

public sealed record ValueListAdapterConfig(
    [property: JsonPropertyName("items")] IReadOnlyList<GhValueListItemConfig>? Items = null,
    [property: JsonPropertyName("selected_index")] int? SelectedIndex = null,
    [property: JsonPropertyName("selected_name")] string? SelectedName = null,
    [property: JsonPropertyName("selected_expression")] string? SelectedExpression = null) : GhComponentAdapterConfig;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GhNumberValue), "number")]
[JsonDerivedType(typeof(GhIntegerValue), "integer")]
[JsonDerivedType(typeof(GhBooleanValue), "boolean")]
[JsonDerivedType(typeof(GhStringValue), "string")]
[JsonDerivedType(typeof(GhPoint3dValue), "point3d")]
[JsonDerivedType(typeof(GhVector3dValue), "vector3d")]
[JsonDerivedType(typeof(GhColorValue), "color")]
[JsonDerivedType(typeof(GhListValue), "list")]
public abstract record GhValue;

public sealed record GhNumberValue(
    [property: JsonPropertyName("value")] double Value) : GhValue;

public sealed record GhIntegerValue(
    [property: JsonPropertyName("value")] int Value) : GhValue;

public sealed record GhBooleanValue(
    [property: JsonPropertyName("value")] bool Value) : GhValue;

public sealed record GhStringValue(
    [property: JsonPropertyName("value")] string Value) : GhValue;

public sealed record GhPoint3dValue(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("z")] double Z) : GhValue;

public sealed record GhVector3dValue(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("z")] double Z) : GhValue;

public sealed record GhColorValue(
    [property: JsonPropertyName("r")] int R,
    [property: JsonPropertyName("g")] int G,
    [property: JsonPropertyName("b")] int B,
    [property: JsonPropertyName("a")] int A = 255) : GhValue;

public sealed record GhListValue(
    [property: JsonPropertyName("items")] IReadOnlyList<GhValue> Items) : GhValue;

public sealed record GhConfigureComponentV2Request(
    string ComponentId,
    GhComponentConfigV2 Config) : IToolRequest;

public sealed record GhConfigureComponentV2Response(
    string ComponentId,
    bool Applied,
    GhComponentConfigV2 NormalizedConfig,
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
