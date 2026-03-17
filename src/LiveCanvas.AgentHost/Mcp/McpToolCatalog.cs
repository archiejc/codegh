namespace LiveCanvas.AgentHost.Mcp;

internal static class McpToolCatalog
{
    public static readonly IReadOnlyList<McpToolDescriptor> All =
    [
        Tool(
            "gh_session_info",
            "Report Rhino 8 and Grasshopper session state for the LiveCanvas runtime.",
            Schema()),
        Tool(
            "gh_new_document",
            "Create a new empty LiveCanvas-owned Grasshopper document.",
            Schema(
                [("name", StringSchema("Optional display name for the new Grasshopper document."))])),
        Tool(
            "gh_list_allowed_components",
            "List the native Grasshopper components allowed in LiveCanvas v0, including canonical port names and config fields.",
            Schema()),
        Tool(
            "gh_add_component",
            "Add one allowed native Grasshopper component at a deterministic canvas position.",
            Schema(
                [
                    ("component_key", StringSchema("Canonical whitelist key returned by gh_list_allowed_components.")),
                    ("x", NumberSchema("Canvas x coordinate in pixels.")),
                    ("y", NumberSchema("Canvas y coordinate in pixels."))
                ],
                required: ["component_key", "x", "y"])),
        Tool(
            "gh_configure_component",
            "Configure a previously added Grasshopper component using the narrow LiveCanvas v0 config contract.",
            Schema(
                [
                    ("component_id", StringSchema("Component identifier returned by gh_add_component.")),
                    ("config", ConfigSchema())
                ],
                required: ["component_id", "config"])),
        Tool(
            "gh_connect",
            "Connect two component ports by canonical port names.",
            Schema(
                [
                    ("source_id", StringSchema("Source component identifier.")),
                    ("source_output", StringSchema("Canonical source output port name.")),
                    ("target_id", StringSchema("Target component identifier.")),
                    ("target_input", StringSchema("Canonical target input port name."))
                ],
                required: ["source_id", "source_output", "target_id", "target_input"])),
        Tool(
            "gh_delete_component",
            "Delete a component from the LiveCanvas-owned Grasshopper document.",
            Schema(
                [("component_id", StringSchema("Component identifier to delete."))],
                required: ["component_id"])),
        Tool(
            "gh_solve",
            "Solve the current LiveCanvas-owned Grasshopper document and return runtime messages.",
            Schema(
                [("expire_all", BooleanSchema("Whether to expire all objects before solving."))])),
        Tool(
            "gh_inspect_document",
            "Inspect the current LiveCanvas-owned Grasshopper document state, including components, connections, runtime messages, and bounds.",
            Schema(
                [
                    ("include_connections", BooleanSchema("Include connection snapshots in the response.")),
                    ("include_runtime_messages", BooleanSchema("Include runtime messages in the response."))
                ])),
        Tool(
            "gh_capture_preview",
            "Capture the active Rhino viewport preview for the current LiveCanvas-owned Grasshopper document.",
            Schema(
                [
                    ("path", StringSchema("Absolute image path for the preview capture (.png, .jpg, .jpeg).")),
                    ("width", NumberSchema("Capture width in pixels.")),
                    ("height", NumberSchema("Capture height in pixels."))
                ],
                required: ["path", "width", "height"])),
        Tool(
            "gh_save_document",
            "Save the current LiveCanvas-owned Grasshopper document to an absolute .gh path.",
            Schema(
                [("path", StringSchema("Absolute .gh output path."))],
                required: ["path"]))
    ];

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

    private static object BooleanSchema(string description) => new
    {
        type = "boolean",
        description
    };

    private static object ConfigSchema() => new
    {
        type = "object",
        description = "Narrow v0 config object. Only Number Slider, Panel, and Colour Swatch support additional fields beyond nickname.",
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
}
