using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Core.ReferenceInterpretation;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.Core.Repair;

public sealed class RepairEngine
{
    public const int MaxIterations = 3;

    public RepairIterationResult Repair(string currentTemplate, string failureKind, ReferenceBrief brief, int iteration)
    {
        if (iteration >= MaxIterations)
        {
            return new RepairIterationResult(false, currentTemplate, ["repair_budget_exhausted"], true);
        }

        return failureKind switch
        {
            "invalid_connection" => new RepairIterationResult(true, currentTemplate, ["replace_illegal_connection"], false),
            "oversized_dimensions" => new RepairIterationResult(
                true,
                currentTemplate,
                [$"clamp_width_depth_to_{DimensionClampPolicy.MaxWidthOrDepth}", $"clamp_height_to_{DimensionClampPolicy.MaxHeight}"],
                false),
            "orphan_component" => new RepairIterationResult(true, currentTemplate, ["delete_orphan_component"], false),
            "loft_failure" when currentTemplate == MassingTemplate.LoftedTaper =>
                new RepairIterationResult(true, MassingTemplate.StackedBars, ["downgrade_loft_to_stacked_bars"], false),
            "subgraph_failure" => new RepairIterationResult(true, currentTemplate, ["regenerate_failed_subgraph"], false),
            _ => new RepairIterationResult(false, currentTemplate, ["no_repair_rule"], false)
        };
    }
}
