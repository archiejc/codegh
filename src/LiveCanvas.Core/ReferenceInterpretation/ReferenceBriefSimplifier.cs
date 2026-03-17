using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.Core.ReferenceInterpretation;

public sealed class ReferenceBriefSimplifier
{
    public ReferenceBrief Simplify(ReferenceBrief brief)
    {
        var massingStrategy = SelectTemplate(brief);
        var dimensions = DimensionClampPolicy.Clamp(brief.ApproxDimensions);
        var stepCount = brief.Leveling.StepCount is null
            ? (int?)null
            : DimensionClampPolicy.ClampStepCount(brief.Leveling.StepCount.Value);

        return brief with
        {
            MassingStrategy = massingStrategy,
            ApproxDimensions = dimensions,
            Leveling = brief.Leveling with { StepCount = stepCount }
        };
    }

    private static string SelectTemplate(ReferenceBrief brief)
    {
        var strategy = brief.MassingStrategy.Trim().ToLowerInvariant();

        return strategy switch
        {
            "single_extrusion" or "single_volume" => MassingTemplate.SingleExtrusion,
            "podium_tower" => MassingTemplate.PodiumTower,
            "stepped" or "stepped_extrusions" or "stepped_tower" => MassingTemplate.SteppedExtrusions,
            "stacked_bars" => MassingTemplate.StackedBars,
            "lofted_taper" or "tapered" => MassingTemplate.LoftedTaper,
            _ when brief.TransformHints.TaperRatio is < 1.0 => MassingTemplate.LoftedTaper,
            _ when brief.Leveling.PodiumHeight is > 0 => MassingTemplate.PodiumTower,
            _ => MassingTemplate.SingleExtrusion
        };
    }
}
