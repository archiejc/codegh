using FluentAssertions;
using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Planner;
using LiveCanvas.Core.ReferenceInterpretation;

namespace LiveCanvas.Core.Tests.Planner;

public class TemplatePlannerTests
{
    private readonly TemplatePlanner planner = new();

    [Fact]
    public void returns_single_extrusion_plan()
    {
        var plan = planner.CreatePlan(MakeBrief(MassingTemplate.SingleExtrusion));

        plan.TemplateName.Should().Be(MassingTemplate.SingleExtrusion);
        plan.Components.Select(component => component.ComponentKey).Should().Contain(V0ComponentKeys.Extrude);
    }

    [Fact]
    public void returns_lofted_taper_plan()
    {
        var plan = planner.CreatePlan(MakeBrief(MassingTemplate.LoftedTaper));

        plan.TemplateName.Should().Be(MassingTemplate.LoftedTaper);
        plan.Components.Select(component => component.ComponentKey).Should().Contain(V0ComponentKeys.Loft);
    }

    [Fact]
    public void podium_tower_plan_emits_two_extrusions()
    {
        var plan = planner.CreatePlan(MakeBrief(MassingTemplate.PodiumTower));

        plan.TemplateName.Should().Be(MassingTemplate.PodiumTower);
        plan.Components.Count(component => component.ComponentKey == V0ComponentKeys.Extrude).Should().Be(2);
        plan.Components.Select(component => component.Alias).Should().Contain(["podium_extrude", "tower_extrude"]);
    }

    [Fact]
    public void stepped_extrusions_plan_respects_step_count_range()
    {
        var plan = planner.CreatePlan(MakeBrief(MassingTemplate.SteppedExtrusions, stepCount: 4));

        plan.TemplateName.Should().Be(MassingTemplate.SteppedExtrusions);
        plan.Components.Count(component => component.ComponentKey == V0ComponentKeys.Extrude).Should().Be(4);
        plan.Components.Select(component => component.Alias).Should().Contain("tier_4_extrude");
    }

    [Fact]
    public void stacked_bars_plan_emits_three_bars_with_offsets()
    {
        var plan = planner.CreatePlan(MakeBrief(MassingTemplate.StackedBars));

        plan.TemplateName.Should().Be(MassingTemplate.StackedBars);
        plan.Components.Count(component => component.ComponentKey == V0ComponentKeys.Extrude).Should().Be(3);
        plan.Components.Select(component => component.Alias).Should().Contain(["bar_2_offset_x", "bar_2_offset_y", "bar_3_offset_x", "bar_3_offset_y"]);
    }

    [Fact]
    public void emits_deterministic_canvas_positions()
    {
        var plan = planner.CreatePlan(MakeBrief(MassingTemplate.SingleExtrusion));
        var first = plan.Components.First(component => component.Alias == "plane");
        var second = plan.Components.First(component => component.Alias == "rect");

        first.X.Should().Be(0);
        first.Y.Should().Be(0);
        second.X.Should().Be(CanvasLayoutPolicy.HorizontalSpacing);
    }

    [Fact]
    public void never_emits_non_whitelisted_component()
    {
        var allowed = new AllowedComponentRegistry().All().Select(component => component.ComponentKey).ToHashSet();
        var plan = planner.CreatePlan(MakeBrief(MassingTemplate.StackedBars));

        plan.Components.Select(component => component.ComponentKey).Should().OnlyContain(key => allowed.Contains(key));
    }

    private static ReferenceBrief MakeBrief(string template, int? stepCount = 3) =>
        new(
            BuildingType: "tower",
            SiteContext: "urban",
            MassingStrategy: template,
            ApproxDimensions: new ApproxDimensions(30, 20, 90),
            Leveling: new LevelingHints(18, 72, stepCount),
            TransformHints: new TransformHints(0, 1.0, null),
            StyleHints: new StyleHints([200, 200, 210], "clean"),
            Confidence: 0.8,
            Assumptions: ["test"]);
}
