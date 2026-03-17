namespace LiveCanvas.Core.Repair;

public sealed record RepairIterationResult(
    bool Repaired,
    string NextTemplate,
    IReadOnlyList<string> Actions,
    bool ExhaustedBudget);
