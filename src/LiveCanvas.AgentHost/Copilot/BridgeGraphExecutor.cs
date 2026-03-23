using LiveCanvas.AgentHost.ToolHandlers;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Contracts.Planner;
using LiveCanvas.Core.ReferenceInterpretation;

namespace LiveCanvas.AgentHost.Copilot;

public sealed class BridgeGraphExecutor(
    GhNewDocumentTool newDocumentTool,
    GhAddComponentTool addComponentTool,
    GhConfigureComponentTool configureComponentTool,
    GhConnectTool connectTool,
    GhSolveTool solveTool,
    GhInspectDocumentTool inspectDocumentTool,
    GhCapturePreviewTool capturePreviewTool,
    GhSaveDocumentTool saveDocumentTool)
{
    public async Task<BridgeGraphExecutionResult> ExecuteAsync(
        TemplateGraphPlan graphPlan,
        string documentName,
        string outputDirectory,
        int previewWidth,
        int previewHeight,
        bool expireAll,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var previewPath = Path.Combine(outputDirectory, "preview.png");
        var documentPath = Path.Combine(outputDirectory, "document.gh");
        SafeDelete(previewPath);
        SafeDelete(documentPath);

        GhNewDocumentResponse? newDocument = null;
        GhSolveResponse? solve = null;
        GhInspectDocumentResponse? inspect = null;
        var warnings = new List<string>();

        try
        {
            newDocument = await newDocumentTool.HandleAsync(documentName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Failure("subgraph_failure", ex.Message, warnings, newDocument, solve, inspect, null, null);
        }

        var componentIds = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            foreach (var component in graphPlan.Components)
            {
                var addResponse = await addComponentTool.HandleAsync(component.ComponentKey, component.X, component.Y, cancellationToken).ConfigureAwait(false);
                componentIds[component.Alias] = addResponse.ComponentId;
                _ = await configureComponentTool.HandleAsync(addResponse.ComponentId, component.Config, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            return Failure("subgraph_failure", ex.Message, warnings, newDocument, solve, inspect, null, null);
        }

        try
        {
            foreach (var connection in graphPlan.Connections)
            {
                _ = await connectTool.HandleAsync(
                    componentIds[connection.SourceAlias],
                    connection.SourceOutput,
                    componentIds[connection.TargetAlias],
                    connection.TargetInput,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            return Failure("invalid_connection", ex.Message, warnings, newDocument, solve, inspect, null, null);
        }

        try
        {
            solve = await solveTool.HandleAsync(expireAll, cancellationToken).ConfigureAwait(false);
            if (!solve.Solved || solve.ErrorCount > 0 || string.Equals(solve.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                return Failure("subgraph_failure", "gh_solve did not complete successfully.", warnings, newDocument, solve, inspect, null, null);
            }
        }
        catch (Exception ex)
        {
            return Failure("subgraph_failure", ex.Message, warnings, newDocument, solve, inspect, null, null);
        }

        try
        {
            inspect = await inspectDocumentTool.HandleAsync(includeConnections: true, includeRuntimeMessages: true, cancellationToken).ConfigureAwait(false);
            if (!inspect.PreviewSummary.HasGeometry)
            {
                var failureKind = string.Equals(graphPlan.TemplateName, MassingTemplate.LoftedTaper, StringComparison.Ordinal)
                    ? "loft_failure"
                    : "subgraph_failure";
                return Failure(failureKind, "gh_inspect_document did not report preview geometry.", warnings, newDocument, solve, inspect, null, null);
            }
        }
        catch (Exception ex)
        {
            return Failure("subgraph_failure", ex.Message, warnings, newDocument, solve, inspect, null, null);
        }

        string? finalPreviewPath = null;
        string? finalDocumentPath = null;

        try
        {
            var capture = await capturePreviewTool.HandleAsync(previewPath, previewWidth, previewHeight, cancellationToken).ConfigureAwait(false);
            if (!capture.Captured)
            {
                throw new InvalidOperationException("gh_capture_preview did not report a captured image.");
            }

            finalPreviewPath = previewPath;
        }
        catch (Exception ex)
        {
            warnings.Add($"capture_failed: {ex.Message}");
            SafeDelete(previewPath);
            return Failure("subgraph_failure", ex.Message, warnings, newDocument, solve, inspect, null, null);
        }

        try
        {
            var save = await saveDocumentTool.HandleAsync(documentPath, cancellationToken).ConfigureAwait(false);
            if (!save.Saved)
            {
                throw new InvalidOperationException("gh_save_document did not report a successful save.");
            }

            finalDocumentPath = documentPath;
        }
        catch (Exception ex)
        {
            warnings.Add($"save_failed: {ex.Message}");
            SafeDelete(documentPath);
            return Failure("subgraph_failure", ex.Message, warnings, newDocument, solve, inspect, null, null);
        }

        return new BridgeGraphExecutionResult(
            Succeeded: true,
            FailureKind: null,
            FailureMessage: null,
            NewDocument: newDocument,
            Solve: solve,
            Inspect: inspect,
            PreviewPath: finalPreviewPath,
            DocumentPath: finalDocumentPath,
            Warnings: warnings);
    }

    private static BridgeGraphExecutionResult Failure(
        string failureKind,
        string message,
        IReadOnlyList<string> warnings,
        GhNewDocumentResponse? newDocument,
        GhSolveResponse? solve,
        GhInspectDocumentResponse? inspect,
        string? previewPath,
        string? documentPath) =>
        new(
            Succeeded: false,
            FailureKind: failureKind,
            FailureMessage: message,
            NewDocument: newDocument,
            Solve: solve,
            Inspect: inspect,
            PreviewPath: previewPath,
            DocumentPath: documentPath,
            Warnings: warnings);

    private static void SafeDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

public sealed record BridgeGraphExecutionResult(
    bool Succeeded,
    string? FailureKind,
    string? FailureMessage,
    GhNewDocumentResponse? NewDocument,
    GhSolveResponse? Solve,
    GhInspectDocumentResponse? Inspect,
    string? PreviewPath,
    string? DocumentPath,
    IReadOnlyList<string> Warnings);
