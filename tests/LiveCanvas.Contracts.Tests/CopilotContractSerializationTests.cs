using System.Text.Json;
using FluentAssertions;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Copilot;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Contracts.Planner;
using LiveCanvas.Contracts.ReferenceInterpretation;

namespace LiveCanvas.Contracts.Tests;

public class CopilotContractSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void copilot_execution_plan_serializes_required_fields()
    {
        var payload = CreateExecutionPlan();

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        json.Should().Contain("schema_version");
        json.Should().Contain("input_prompt");
        json.Should().Contain("input_images");
        json.Should().Contain("reference_brief");
        json.Should().Contain("template_name");
        json.Should().Contain("graph_plan");
        json.Should().Contain("assumptions");
        json.Should().Contain("warnings");
        json.Should().Contain("suggested_document_name");
    }

    [Fact]
    public void copilot_request_and_response_models_round_trip_without_field_loss()
    {
        var executionPlan = CreateExecutionPlan();
        var planRequest = new CopilotPlanRequest("make a stepped tower", ["/tmp/reference.png"]);
        var planResponse = new CopilotPlanResponse(executionPlan);
        var applyRequest = new CopilotApplyPlanRequest(executionPlan, "/tmp/out", 1200, 800, false);
        var applyResponse = new CopilotApplyPlanResponse(
            Status: "succeeded",
            RepairIterations: 1,
            RepairActions: ["downgrade_loft_to_stacked_bars"],
            NewDocument: new GhNewDocumentResponse("doc_1", "Tower", true),
            Solve: new GhSolveResponse(true, "ok", 12, 0, 0, []),
            Inspect: new GhInspectDocumentResponse(
                "doc_1",
                [],
                [],
                [],
                new GhBounds(0, 0, 0, 10, 10, 10),
                new GhPreviewSummary(true, 1)),
            PreviewPath: "/tmp/out/preview.png",
            DocumentPath: "/tmp/out/document.gh",
            Warnings: []);

        RoundTrip(planRequest).Should().BeEquivalentTo(planRequest);
        RoundTrip(planResponse).Should().BeEquivalentTo(planResponse);
        RoundTrip(applyRequest).Should().BeEquivalentTo(applyRequest);
        RoundTrip(applyResponse).Should().BeEquivalentTo(applyResponse);
    }

    private static T RoundTrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }

    private static CopilotExecutionPlan CreateExecutionPlan() =>
        new(
            InputPrompt: "make a tapered tower",
            InputImages: ["/tmp/reference.png"],
            ReferenceBrief: new ReferenceBrief(
                BuildingType: "tower",
                SiteContext: "urban",
                MassingStrategy: "lofted_taper",
                ApproxDimensions: new ApproxDimensions(30, 20, 90),
                Leveling: new LevelingHints(18, 72, 3),
                TransformHints: new TransformHints(0, 0.75, null),
                StyleHints: new StyleHints([255, 255, 255], "clean"),
                Confidence: 0.8,
                Assumptions: ["metric units"]),
            TemplateName: "lofted_taper",
            GraphPlan: new TemplateGraphPlan(
                "lofted_taper",
                [
                    new GraphComponentPlan("base_width", "number_slider", 0, 0, new GhComponentConfig(Slider: new SliderConfig(5, 200, 30, false)))
                ],
                []),
            Assumptions: ["metric units"],
            Warnings: ["dimensions_clamped"],
            SuggestedDocumentName: "tapered-tower");
}
