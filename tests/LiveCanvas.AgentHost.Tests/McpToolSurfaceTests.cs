using FluentAssertions;
using LiveCanvas.AgentHost.Mcp;

namespace LiveCanvas.AgentHost.Tests;

public class McpToolSurfaceTests
{
    [Fact]
    public void mcp_tool_surface_matches_spec_exactly()
    {
        ToolDefinitions.All.Should().HaveCount(13);
        ToolDefinitions.All.Should().Equal(
            "gh_session_info",
            "gh_new_document",
            "gh_list_allowed_components",
            "gh_add_component",
            "gh_configure_component",
            "gh_connect",
            "gh_delete_component",
            "gh_solve",
            "gh_inspect_document",
            "gh_capture_preview",
            "gh_save_document",
            "copilot_plan",
            "copilot_apply_plan");
    }

    [Fact]
    public void mcp_tool_catalog_matches_public_surface_exactly()
    {
        McpToolCatalog.All.Select(tool => tool.Name).Should().Equal(ToolDefinitions.All);
    }
}
