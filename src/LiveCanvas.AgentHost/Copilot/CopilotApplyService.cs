using LiveCanvas.Contracts.Copilot;
using LiveCanvas.Contracts.Planner;
using LiveCanvas.Core.Planner;
using LiveCanvas.Core.Repair;

namespace LiveCanvas.AgentHost.Copilot;

public sealed class CopilotApplyService(
    CopilotExecutionPlanValidator executionPlanValidator,
    BridgeGraphExecutor bridgeGraphExecutor,
    TemplatePlanner templatePlanner,
    TemplateGraphParameterizer parameterizer,
    RepairEngine repairEngine,
    CopilotFailureClassifier failureClassifier) : ICopilotApplyService
{
    public async Task<CopilotApplyPlanResponse> ApplyPlanAsync(CopilotApplyPlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExecutionPlan);

        executionPlanValidator.EnsureCompatible(request.ExecutionPlan);

        var outputDirectory = ResolveOutputDirectory(request.OutputDir);
        var previewWidth = request.PreviewWidth ?? 1600;
        var previewHeight = request.PreviewHeight ?? 900;
        var expireAll = request.ExpireAll ?? true;

        if (previewWidth <= 0 || previewHeight <= 0)
        {
            throw new ArgumentException("preview_width and preview_height must be positive integers.");
        }

        var warnings = new List<string>(request.ExecutionPlan.Warnings ?? []);
        var repairActions = new List<string>();
        var repairIterations = 0;
        var currentBrief = request.ExecutionPlan.ReferenceBrief;
        var currentTemplate = request.ExecutionPlan.TemplateName;
        var currentGraphPlan = request.ExecutionPlan.GraphPlan;
        BridgeGraphExecutionResult? lastExecution = null;

        while (true)
        {
            TemplateGraphPlan normalizedPlan;

            try
            {
                normalizedPlan = executionPlanValidator.ValidateAndNormalizeGraph(currentGraphPlan);
            }
            catch (Exception ex) when (ex is CopilotGraphValidationException)
            {
                var response = TryRepairOrFinish(
                    request,
                    currentBrief,
                    ref currentTemplate,
                    ref currentGraphPlan,
                    outputDirectory,
                    warnings,
                    repairActions,
                    ref repairIterations,
                    failureClassifier.Classify(ex),
                    ex.Message);

                if (response is not null)
                {
                    return response;
                }

                continue;
            }

            lastExecution = await bridgeGraphExecutor.ExecuteAsync(
                normalizedPlan,
                request.ExecutionPlan.SuggestedDocumentName,
                outputDirectory,
                previewWidth,
                previewHeight,
                expireAll,
                cancellationToken).ConfigureAwait(false);

            AddWarnings(warnings, lastExecution.Warnings);
            if (lastExecution.Succeeded)
            {
                return BuildResponse("succeeded", repairIterations, repairActions, lastExecution, warnings);
            }

            var failureKind = failureClassifier.Classify(currentTemplate, lastExecution);
            var responseAfterFailure = TryRepairOrFinish(
                request,
                currentBrief,
                ref currentTemplate,
                ref currentGraphPlan,
                outputDirectory,
                warnings,
                repairActions,
                ref repairIterations,
                failureKind,
                lastExecution.FailureMessage);

            if (responseAfterFailure is not null)
            {
                return BuildResponse(
                    responseAfterFailure.Status,
                    responseAfterFailure.RepairIterations,
                    responseAfterFailure.RepairActions,
                    lastExecution,
                    responseAfterFailure.Warnings);
            }

            currentBrief = currentBrief with { MassingStrategy = currentTemplate };
        }
    }

    private CopilotApplyPlanResponse? TryRepairOrFinish(
        CopilotApplyPlanRequest request,
        LiveCanvas.Contracts.ReferenceInterpretation.ReferenceBrief currentBrief,
        ref string currentTemplate,
        ref LiveCanvas.Contracts.Planner.TemplateGraphPlan currentGraphPlan,
        string outputDirectory,
        List<string> warnings,
        List<string> repairActions,
        ref int repairIterations,
        string failureKind,
        string? failureMessage)
    {
        if (!string.IsNullOrWhiteSpace(failureMessage))
        {
            AddWarnings(warnings, [$"{failureKind}: {failureMessage}"]);
        }

        var repair = repairEngine.Repair(currentTemplate, failureKind, currentBrief, repairIterations);
        if (repair.ExhaustedBudget)
        {
            AddWarnings(warnings, repair.Actions);
            return BuildResponse("repair_exhausted", repairIterations, repairActions, null, warnings);
        }

        if (!repair.Repaired)
        {
            AddWarnings(warnings, repair.Actions);
            return BuildResponse("failed", repairIterations, repairActions, null, warnings);
        }

        repairIterations++;
        repairActions.AddRange(repair.Actions);
        currentTemplate = repair.NextTemplate;
        var repairedBrief = currentBrief with { MassingStrategy = currentTemplate };
        currentGraphPlan = parameterizer.Parameterize(templatePlanner.CreatePlan(repairedBrief), repairedBrief);
        return null;
    }

    private static CopilotApplyPlanResponse BuildResponse(
        string status,
        int repairIterations,
        IReadOnlyList<string> repairActions,
        BridgeGraphExecutionResult? execution,
        IReadOnlyList<string> warnings) =>
        new(
            Status: status,
            RepairIterations: repairIterations,
            RepairActions: repairActions.ToArray(),
            NewDocument: execution?.NewDocument,
            Solve: execution?.Solve,
            Inspect: execution?.Inspect,
            PreviewPath: execution?.PreviewPath,
            DocumentPath: execution?.DocumentPath,
            Warnings: warnings.ToArray());

    private static void AddWarnings(List<string> warnings, IEnumerable<string> newWarnings)
    {
        foreach (var warning in newWarnings.Where(warning => !string.IsNullOrWhiteSpace(warning)))
        {
            if (!warnings.Contains(warning, StringComparer.Ordinal))
            {
                warnings.Add(warning);
            }
        }
    }

    private static string ResolveOutputDirectory(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Path.Combine(Path.GetTempPath(), "livecanvas-copilot", Guid.NewGuid().ToString("N"));
        }

        if (!Path.IsPathRooted(outputDirectory))
        {
            throw new ArgumentException("output_dir must be an absolute path.");
        }

        return outputDirectory;
    }
}
