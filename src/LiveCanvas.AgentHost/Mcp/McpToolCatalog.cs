namespace LiveCanvas.AgentHost.Mcp;

internal static class McpToolCatalog
{
    public static readonly IReadOnlyList<McpToolDescriptor> All = BuildCatalog();

    private static IReadOnlyList<McpToolDescriptor> BuildCatalog()
    {
        var descriptorsByName = new Dictionary<string, McpToolDescriptor>
        {
            [ToolDefinitions.GhSessionInfo] = Tool(
                ToolDefinitions.GhSessionInfo,
                "Report Rhino 8 and Grasshopper session state for the LiveCanvas runtime.",
                Schema()),
            [ToolDefinitions.GhNewDocument] = Tool(
                ToolDefinitions.GhNewDocument,
                "Create a new empty LiveCanvas-owned Grasshopper document.",
                Schema(
                    [("name", StringSchema("Optional display name for the new Grasshopper document."))])),
            [ToolDefinitions.GhListAllowedComponents] = Tool(
                ToolDefinitions.GhListAllowedComponents,
                "List the Grasshopper components available to LiveCanvas, including canonical port names, legacy config fields, and v2 config ops when available.",
                Schema()),
            [ToolDefinitions.GhAddComponent] = Tool(
                ToolDefinitions.GhAddComponent,
                "Add one available Grasshopper component at a deterministic canvas position.",
                Schema(
                    [
                        ("component_key", StringSchema("Component key returned by gh_list_allowed_components.")),
                        ("x", NumberSchema("Canvas x coordinate in pixels.")),
                        ("y", NumberSchema("Canvas y coordinate in pixels."))
                    ],
                    required: ["component_key", "x", "y"])),
            [ToolDefinitions.GhConfigureComponent] = Tool(
                ToolDefinitions.GhConfigureComponent,
                "Configure a previously added Grasshopper component using the narrow LiveCanvas v0 config contract.",
                Schema(
                    [
                        ("component_id", StringSchema("Component identifier returned by gh_add_component.")),
                        ("config", ConfigSchema())
                    ],
                    required: ["component_id", "config"])),
            [ToolDefinitions.GhConfigureComponentV2] = Tool(
                ToolDefinitions.GhConfigureComponentV2,
                "Configure a previously added Grasshopper component using the extensible v2 op-based config contract.",
                Schema(
                    [
                        ("component_id", StringSchema("Component identifier returned by gh_add_component.")),
                        ("config", ConfigV2Schema())
                    ],
                    required: ["component_id", "config"])),
            [ToolDefinitions.GhConnect] = Tool(
                ToolDefinitions.GhConnect,
                "Connect two component ports by canonical port names.",
                Schema(
                    [
                        ("source_id", StringSchema("Source component identifier.")),
                        ("source_output", StringSchema("Canonical source output port name.")),
                        ("target_id", StringSchema("Target component identifier.")),
                        ("target_input", StringSchema("Canonical target input port name."))
                    ],
                    required: ["source_id", "source_output", "target_id", "target_input"])),
            [ToolDefinitions.GhDeleteComponent] = Tool(
                ToolDefinitions.GhDeleteComponent,
                "Delete a component from the LiveCanvas-owned Grasshopper document.",
                Schema(
                    [("component_id", StringSchema("Component identifier to delete."))],
                    required: ["component_id"])),
            [ToolDefinitions.GhSolve] = Tool(
                ToolDefinitions.GhSolve,
                "Solve the current LiveCanvas-owned Grasshopper document and return runtime messages.",
                Schema(
                    [("expire_all", BooleanSchema("Whether to expire all objects before solving."))])),
            [ToolDefinitions.GhInspectDocument] = Tool(
                ToolDefinitions.GhInspectDocument,
                "Inspect the current LiveCanvas-owned Grasshopper document state, including components, connections, runtime messages, and bounds.",
                Schema(
                    [
                        ("include_connections", BooleanSchema("Include connection snapshots in the response.")),
                        ("include_runtime_messages", BooleanSchema("Include runtime messages in the response."))
                    ])),
            [ToolDefinitions.GhCapturePreview] = Tool(
                ToolDefinitions.GhCapturePreview,
                "Capture the active Rhino viewport preview for the current LiveCanvas-owned Grasshopper document.",
                Schema(
                    [
                        ("path", StringSchema("Absolute image path for the preview capture (.png, .jpg, .jpeg).")),
                        ("width", IntegerSchema("Capture width in pixels.")),
                        ("height", IntegerSchema("Capture height in pixels."))
                    ],
                    required: ["path", "width", "height"])),
            [ToolDefinitions.GhSaveDocument] = Tool(
                ToolDefinitions.GhSaveDocument,
                "Save the current LiveCanvas-owned Grasshopper document to an absolute .gh path.",
                Schema(
                    [("path", StringSchema("Absolute .gh output path."))],
                    required: ["path"])),
            [ToolDefinitions.CopilotPlan] = Tool(
                ToolDefinitions.CopilotPlan,
                "Create a copilot execution plan from text and optional local images without touching Rhino or Grasshopper.",
                Schema(
                    [
                        ("prompt", StringSchema("Required copilot prompt.")),
                        ("image_paths", new
                        {
                            type = "array",
                            description = "Optional absolute local image paths (.png, .jpg, .jpeg), up to 4 items.",
                            items = StringSchema("Absolute local image path.")
                        })
                    ],
                    required: ["prompt"])),
            [ToolDefinitions.CopilotApplyPlan] = Tool(
                ToolDefinitions.CopilotApplyPlan,
                "Execute a server-emitted copilot execution plan through the LiveCanvas Rhino and Grasshopper bridge.",
                Schema(
                    [
                        ("execution_plan", new
                        {
                            type = "object",
                            description = "A server-emitted copilot execution plan."
                        }),
                        ("output_dir", StringSchema("Optional absolute output directory.")),
                        ("preview_width", IntegerSchema("Optional preview width in pixels.")),
                        ("preview_height", IntegerSchema("Optional preview height in pixels.")),
                        ("expire_all", BooleanSchema("Whether to expire all objects before solving."))
                    ],
                    required: ["execution_plan"]))
        };

        return ToolDefinitions.All
            .Select(name => descriptorsByName.TryGetValue(name, out var descriptor)
                ? descriptor
                : throw new InvalidOperationException($"No MCP descriptor registered for tool '{name}'."))
            .ToArray();
    }

    private static McpToolDescriptor Tool(string name, string description, object inputSchema) =>
        new(name, description, inputSchema);

    private static object Schema() => Schema(Array.Empty<(string Name, object Schema)>());

    private static object Schema((string Name, object Schema)[] properties, IReadOnlyCollection<string>? required = null) =>
        new
        {
            type = "object",
            properties = properties.ToDictionary(property => property.Name, property => property.Schema),
            required = required ?? Array.Empty<string>()
        };

    private static object StringSchema(string description) => new
    {
        type = "string",
        description
    };

    private static object NumberSchema(string description) => new
    {
        type = "number",
        description
    };

    private static object IntegerSchema(string description) => new
    {
        type = "integer",
        description
    };

    private static object BooleanSchema(string description) => new
    {
        type = "boolean",
        description
    };

    private static object ConfigSchema() => new
    {
        type = "object",
        description = "Legacy v0 config object. Only Number Slider, Panel, and Colour Swatch support additional fields beyond nickname.",
        properties = new Dictionary<string, object>
        {
            ["nickname"] = StringSchema("Optional Grasshopper nickname."),
            ["slider"] = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["min"] = NumberSchema("Slider minimum."),
                    ["max"] = NumberSchema("Slider maximum."),
                    ["value"] = NumberSchema("Slider value."),
                    ["integer"] = BooleanSchema("Whether the slider is integer-only.")
                }
            },
            ["panel"] = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["text"] = StringSchema("Panel text."),
                    ["multiline"] = BooleanSchema("Whether multiline parsing is enabled.")
                }
            },
            ["colour"] = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["r"] = NumberSchema("Red channel 0-255."),
                    ["g"] = NumberSchema("Green channel 0-255."),
                    ["b"] = NumberSchema("Blue channel 0-255."),
                    ["a"] = NumberSchema("Alpha channel 0-255.")
                }
            }
        }
    };

    private static object ConfigV2Schema() => new
    {
        type = "object",
        description = "Extensible component config protocol. Use ops such as set_nickname, set_input_persistent_data, clear_input_persistent_data, set_param_flags, and adapter_config.",
        properties = new Dictionary<string, object>
        {
            ["schema_version"] = StringSchema("Must be gh_component_config/v2."),
            ["ops"] = new
            {
                type = "array",
                description = "Ordered list of component config operations.",
                items = new
                {
                    type = "object",
                    description = "One component config operation."
                }
            }
        },
        required = new[] { "schema_version", "ops" }
    };
}
