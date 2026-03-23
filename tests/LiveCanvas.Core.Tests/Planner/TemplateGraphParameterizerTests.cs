using FluentAssertions;
using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Core.Planner;
using LiveCanvas.Core.ReferenceInterpretation;

namespace LiveCanvas.Core.Tests.Planner;

public class TemplateGraphParameterizerTests
{
    private readonly TemplatePlanner planner = new();
    private readonly TemplateGraphParameterizer parameterizer = new();

    [Fact]
    public void parameterizer_applies_single_extrusion_dimensions()
    {
        var brief = MakeBrief(MassingTemplate.SingleExtrusion, new ApproxDimensions(42, 28, 120), 3);
        var plan = parameterizer.Parameterize(planner.CreatePlan(brief), brief);

        GetSliderValue(plan, "width").Should().Be(42);
        GetSliderValue(plan, "depth").Should().Be(28);
        GetSliderValue(plan, "height").Should().Be(120);
    }

    [Fact]
    public void parameterizer_applies_podium_tower_specific_values()
    {
        var brief = MakeBrief(MassingTemplate.PodiumTower, new ApproxDimensions(40, 30, 100), 3);
        var plan = parameterizer.Parameterize(planner.CreatePlan(brief), brief);

        GetSliderValue(plan, "podium_width").Should().Be(40);
        GetSliderValue(plan, "podium_depth").Should().Be(30);
        GetSliderValue(plan, "podium_height").Should().Be(20);
        GetSliderValue(plan, "tower_width").Should().Be(24);
        GetSliderValue(plan, "tower_depth").Should().Be(18);
    }

    [Fact]
    public void parameterizer_applies_stepped_and_stacked_aliases()
    {
        var steppedBrief = MakeBrief(MassingTemplate.SteppedExtrusions, new ApproxDimensions(50, 40, 120), 4);
        var steppedPlan = parameterizer.Parameterize(planner.CreatePlan(steppedBrief), steppedBrief);
        GetSliderValue(steppedPlan, "tier_4_width").Should().Be(35);
        GetSliderValue(steppedPlan, "tier_2_offset").Should().Be(60);

        var stackedBrief = MakeBrief(MassingTemplate.StackedBars, new ApproxDimensions(50, 40, 120), 3);
        var stackedPlan = parameterizer.Parameterize(planner.CreatePlan(stackedBrief), stackedBrief);
        GetSliderValue(stackedPlan, "bar_2_offset_x").Should().Be(6);
        GetSliderValue(stackedPlan, "bar_3_offset_x").Should().Be(-5);
        GetSliderValue(stackedPlan, "bar_1_height").Should().Be(54);
    }

    private static double GetSliderValue(LiveCanvas.Contracts.Planner.TemplateGraphPlan plan, string alias) =>
        plan.Components.Single(component => component.Alias == alias).Config.Slider!.Value!.Value;

    private static ReferenceBrief MakeBrief(string template, ApproxDimensions dimensions, int stepCount) =>
        new(
            BuildingType: "tower",
            SiteContext: "urban",
            MassingStrategy: template,
            ApproxDimensions: dimensions,
            Leveling: new LevelingHints(null, null, stepCount),
            TransformHints: new TransformHints(0, 0.7, null),
            StyleHints: new StyleHints([120, 120, 120], "clean"),
            Confidence: 0.9,
            Assumptions: []);
}
