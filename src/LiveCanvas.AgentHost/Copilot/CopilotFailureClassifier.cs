using LiveCanvas.Core.ReferenceInterpretation;

namespace LiveCanvas.AgentHost.Copilot;

public sealed class CopilotFailureClassifier
{
    public string Classify(string currentTemplate, BridgeGraphExecutionResult executionResult)
    {
        if (!string.IsNullOrWhiteSpace(executionResult.FailureKind))
        {
            return executionResult.FailureKind!;
        }

        if (string.Equals(currentTemplate, MassingTemplate.LoftedTaper, StringComparison.Ordinal)
            && executionResult.Inspect?.PreviewSummary.HasGeometry == false)
        {
            return "loft_failure";
        }

        return "subgraph_failure";
    }

    public string Classify(Exception exception) =>
        exception is CopilotGraphValidationException validationException
            ? validationException.FailureKind
            : "subgraph_failure";
}
