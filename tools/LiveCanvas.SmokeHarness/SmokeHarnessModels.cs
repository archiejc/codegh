using System.Text.Json;
using LiveCanvas.Contracts.Session;

namespace LiveCanvas.SmokeHarness;

public sealed record SmokeHarnessOptions(
    string AgentHostProjectPath,
    SmokeHarnessMode Mode = SmokeHarnessMode.Mock,
    SmokeHarnessScenario Scenario = SmokeHarnessScenario.Smoke,
    string? AgentHostDllPath = null,
    bool SkipBuildAgentHost = false,
    string? OutputDirectory = null,
    bool RunBridgeDirectCheck = true,
    bool RunMcpCheck = true,
    string Configuration = "Debug",
    string? BridgeUri = null,
    int LivePreflightTimeoutSeconds = 10);

public sealed record SmokeHarnessResult(
    bool Success,
    string OutputDirectory,
    IReadOnlyList<string> CompletedChecks,
    IReadOnlyList<string> Errors);

public enum SmokeHarnessMode
{
    Mock,
    Live
}

public enum SmokeHarnessScenario
{
    Smoke,
    AbsoluteTowers,
    CopilotAbsoluteTowers
}

internal sealed record SmokeSessionSummary(
    string Platform,
    string? RhinoVersion,
    string ToolVersion);

internal sealed record SmokeArtifactManifest(
    string Mode,
    string Scenario,
    string BridgeUri,
    string OutputDirectory,
    string PreviewPath,
    string GhPath,
    string TranscriptPath,
    IReadOnlyList<string> CompletedChecks,
    bool Success,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset FinishTimeUtc,
    SmokeSessionSummary? SessionSummary);

internal sealed record SmokeTranscriptEvent(
    int Sequence,
    string Phase,
    string Transport,
    string Method,
    JsonElement? Request,
    JsonElement? Response,
    bool Success,
    DateTimeOffset TimestampUtc);

internal sealed class HarnessRunContext
{
    private readonly JsonSerializerOptions jsonOptions;
    private int nextSequence = 1;

    public HarnessRunContext(SmokeHarnessOptions options, string outputDirectory, JsonSerializerOptions jsonOptions)
    {
        Options = options;
        OutputDirectory = outputDirectory;
        this.jsonOptions = jsonOptions;
        PreviewPath = Path.Combine(outputDirectory, "preview.png");
        GhPath = Path.Combine(outputDirectory, GetScenarioFileName(options.Scenario));
        ManifestPath = Path.Combine(outputDirectory, "manifest.json");
        TranscriptPath = Path.Combine(outputDirectory, "transcript.json");
        StartedUtc = DateTimeOffset.UtcNow;
    }

    public SmokeHarnessOptions Options { get; }

    public string OutputDirectory { get; }

    public string PreviewPath { get; }

    public string GhPath { get; }

    public string ManifestPath { get; }

    public string TranscriptPath { get; }

    public string? BridgeUri { get; set; }

    public string? CopilotProviderBaseUrl { get; set; }

    public DateTimeOffset StartedUtc { get; }

    public DateTimeOffset? FinishedUtc { get; set; }

    public SmokeSessionSummary? SessionSummary { get; set; }

    public List<string> CompletedChecks { get; } = [];

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<SmokeTranscriptEvent> Transcript { get; } = [];

    public void AddCompletedCheck(string checkName) => CompletedChecks.Add(checkName);

    public void AddError(string error) => Errors.Add(error);

    public void AddWarning(string warning)
    {
        if (!Warnings.Contains(warning, StringComparer.Ordinal))
        {
            Warnings.Add(warning);
        }
    }

    public void AddEvent(string phase, string transport, string method, object? request, object? response, bool success)
    {
        Transcript.Add(new SmokeTranscriptEvent(
            Sequence: nextSequence++,
            Phase: phase,
            Transport: transport,
            Method: method,
            Request: ToJson(request),
            Response: ToJson(response),
            Success: success,
            TimestampUtc: DateTimeOffset.UtcNow));
    }

    private JsonElement? ToJson(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            JsonElement element => element.Clone(),
            JsonDocument document => document.RootElement.Clone(),
            _ => JsonSerializer.SerializeToElement(value, jsonOptions)
        };
    }

    private static string GetScenarioFileName(SmokeHarnessScenario scenario) =>
        scenario switch
        {
            SmokeHarnessScenario.Smoke => "smoke.gh",
            SmokeHarnessScenario.AbsoluteTowers => "absolute-towers.gh",
            SmokeHarnessScenario.CopilotAbsoluteTowers => "document.gh",
            _ => "scene.gh"
        };
}

internal sealed class SmokeHarnessFailureException : Exception
{
    public SmokeHarnessFailureException(string category, string message, Exception? innerException = null)
        : base($"{category}: {message}", innerException)
    {
        Category = category;
    }

    public string Category { get; }
}
