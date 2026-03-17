using FluentAssertions;
using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Core.ReferenceInterpretation;

namespace LiveCanvas.Core.Tests.ReferenceInterpretation;

public class ReferenceBriefSimplifierTests
{
    private readonly ReferenceBriefSimplifier simplifier = new();

    [Fact]
    public void maps_freeform_tall_rectangular_tower_to_single_extrusion()
    {
        var brief = MakeBrief(massingStrategy: "single_volume");

        simplifier.Simplify(brief).MassingStrategy.Should().Be(MassingTemplate.SingleExtrusion);
    }

    [Fact]
    public void maps_podium_and_slender_tower_to_podium_tower()
    {
        var brief = MakeBrief(massingStrategy: "unknown", podiumHeight: 18);

        simplifier.Simplify(brief).MassingStrategy.Should().Be(MassingTemplate.PodiumTower);
    }

    [Fact]
    public void maps_tapered_reference_to_lofted_taper_when_confident()
    {
        var brief = MakeBrief(massingStrategy: "unknown", taperRatio: 0.8);

        simplifier.Simplify(brief).MassingStrategy.Should().Be(MassingTemplate.LoftedTaper);
    }

    [Fact]
    public void clamps_dimensions_and_step_count()
    {
        var brief = MakeBrief(width: 500, depth: 1, height: 1000, stepCount: 99);

        var simplified = simplifier.Simplify(brief);
        simplified.ApproxDimensions.Should().BeEquivalentTo(new ApproxDimensions(200, 5, 400));
        simplified.Leveling.StepCount.Should().Be(12);
    }

    private static ReferenceBrief MakeBrief(
        string massingStrategy = "unknown",
        double width = 30,
        double depth = 20,
        double height = 90,
        double? podiumHeight = null,
        int? stepCount = 3,
        double? taperRatio = 1.0) =>
        new(
            BuildingType: "tower",
            SiteContext: "urban",
            MassingStrategy: massingStrategy,
            ApproxDimensions: new ApproxDimensions(width, depth, height),
            Leveling: new LevelingHints(podiumHeight, null, stepCount),
            TransformHints: new TransformHints(0, taperRatio, null),
            StyleHints: new StyleHints([200, 200, 200], "clean"),
            Confidence: 0.8,
            Assumptions: ["test"]);
}
