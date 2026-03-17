using FluentAssertions;
using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Core.ReferenceInterpretation;
using LiveCanvas.Core.Repair;

namespace LiveCanvas.Core.Tests.Repair;

public class RepairEngineTests
{
    private readonly RepairEngine repairEngine = new();

    [Fact]
    public void replaces_illegal_connection_when_rule_exists()
    {
        var result = repairEngine.Repair(MassingTemplate.SingleExtrusion, "invalid_connection", MakeBrief(), 0);

        result.Repaired.Should().BeTrue();
        result.Actions.Should().Contain("replace_illegal_connection");
    }

    [Fact]
    public void downgrades_loft_failure_to_stacked_extrusion()
    {
        var result = repairEngine.Repair(MassingTemplate.LoftedTaper, "loft_failure", MakeBrief(), 0);

        result.NextTemplate.Should().Be(MassingTemplate.StackedBars);
    }

    [Fact]
    public void stops_after_three_iterations()
    {
        var result = repairEngine.Repair(MassingTemplate.SingleExtrusion, "invalid_connection", MakeBrief(), 3);

        result.ExhaustedBudget.Should().BeTrue();
    }

    private static ReferenceBrief MakeBrief() =>
        new(
            BuildingType: "tower",
            SiteContext: "urban",
            MassingStrategy: MassingTemplate.SingleExtrusion,
            ApproxDimensions: new ApproxDimensions(30, 20, 90),
            Leveling: new LevelingHints(18, 72, 3),
            TransformHints: new TransformHints(0, 1.0, null),
            StyleHints: new StyleHints([200, 200, 210], "clean"),
            Confidence: 0.8,
            Assumptions: ["test"]);
}
