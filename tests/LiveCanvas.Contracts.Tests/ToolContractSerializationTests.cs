using System.Text.Json;
using FluentAssertions;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Contracts.Session;

namespace LiveCanvas.Contracts.Tests;

public class ToolContractSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void tool_request_and_response_models_round_trip_without_field_loss()
    {
        var payload = new GhSessionInfoResponse(
            RhinoRunning: true,
            RhinoVersion: "8.0",
            Platform: "macos",
            GrasshopperLoaded: true,
            ActiveDocumentName: "Unnamed",
            DocumentObjectCount: 0,
            Units: "Meters",
            ModelTolerance: 0.01,
            ToolVersion: "0.1.0");

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<GhSessionInfoResponse>(json, JsonOptions);

        roundTripped.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void reference_brief_schema_serializes_required_fields()
    {
        var brief = new ReferenceBrief(
            BuildingType: "tower",
            SiteContext: "urban",
            MassingStrategy: "single_extrusion",
            ApproxDimensions: new ApproxDimensions(30, 20, 90),
            Leveling: new LevelingHints(18, 72, 3),
            TransformHints: new TransformHints(0, 1.0, null),
            StyleHints: new StyleHints([200, 200, 210], "clean"),
            Confidence: 0.75,
            Assumptions: ["Assumed metric units"]);

        var json = JsonSerializer.Serialize(brief, JsonOptions);

        json.Should().Contain("buildingType");
        json.Should().Contain("siteContext");
        json.Should().Contain("massingStrategy");
        json.Should().Contain("approxDimensions");
        json.Should().Contain("leveling");
        json.Should().Contain("transformHints");
        json.Should().Contain("styleHints");
        json.Should().Contain("confidence");
        json.Should().Contain("assumptions");
    }

    [Fact]
    public void allowed_component_schema_serializes_config_fields()
    {
        var definition = new AllowedComponentDefinition(
            "number_slider",
            "Number Slider",
            "Input",
            [],
            [new AllowedComponentPortInfo("N", "output", 0)],
            [
                new AllowedComponentConfigField("min", "number", false),
                new AllowedComponentConfigField("max", "number", false),
                new AllowedComponentConfigField("value", "number", false),
                new AllowedComponentConfigField("integer", "boolean", false)
            ]);

        var json = JsonSerializer.Serialize(definition, JsonOptions);

        json.Should().Contain("componentKey");
        json.Should().Contain("configFields");
        json.Should().Contain("integer");
    }
}
