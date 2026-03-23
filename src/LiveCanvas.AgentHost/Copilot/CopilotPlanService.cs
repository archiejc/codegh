using System.Text.Json;
using System.Text.RegularExpressions;
using LiveCanvas.Contracts.Copilot;
using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Core.Planner;
using LiveCanvas.Core.ReferenceInterpretation;

namespace LiveCanvas.AgentHost.Copilot;

public sealed class CopilotPlanService(
    CopilotOptions options,
    ICopilotModelClient modelClient,
    ReferenceBriefSimplifier briefSimplifier,
    TemplatePlanner templatePlanner,
    TemplateGraphParameterizer parameterizer) : ICopilotPlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] AllowedImageExtensions = [".png", ".jpg", ".jpeg"];

    public async Task<CopilotPlanResponse> CreatePlanAsync(CopilotPlanRequest request, CancellationToken cancellationToken = default)
    {
        options.EnsureConfigured("copilot_plan");

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("Argument 'prompt' must be a non-empty string.");
        }

        var inputImages = request.ImagePaths?.ToArray() ?? [];
        if (inputImages.Length > 4)
        {
            throw new ArgumentException("Argument 'image_paths' supports up to 4 images.");
        }

        var dataUrls = new List<string>(inputImages.Length);
        foreach (var imagePath in inputImages)
        {
            dataUrls.Add(await ConvertImagePathToDataUrlAsync(imagePath, cancellationToken));
        }

        var modelJson = await modelClient.CreateReferenceBriefJsonAsync(request.Prompt, dataUrls, cancellationToken);

        ReferenceBrief modelBrief;
        try
        {
            modelBrief = JsonSerializer.Deserialize<ReferenceBrief>(modelJson, JsonOptions)
                ?? throw new ArgumentException("Copilot model returned an empty ReferenceBrief payload.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Copilot model returned invalid JSON for ReferenceBrief.", ex);
        }

        var simplifiedBrief = briefSimplifier.Simplify(modelBrief);
        var warnings = new List<string>();
        if (!Equals(modelBrief.ApproxDimensions, simplifiedBrief.ApproxDimensions))
        {
            warnings.Add("dimensions_clamped");
        }

        var graphPlan = templatePlanner.CreatePlan(simplifiedBrief);
        graphPlan = parameterizer.Parameterize(graphPlan, simplifiedBrief);

        var executionPlan = new CopilotExecutionPlan(
            InputPrompt: request.Prompt,
            InputImages: inputImages,
            ReferenceBrief: simplifiedBrief,
            TemplateName: graphPlan.TemplateName,
            GraphPlan: graphPlan,
            Assumptions: simplifiedBrief.Assumptions ?? [],
            Warnings: warnings,
            SuggestedDocumentName: SuggestDocumentName(request.Prompt, graphPlan.TemplateName));

        return new CopilotPlanResponse(executionPlan);
    }

    private static async Task<string> ConvertImagePathToDataUrlAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !Path.IsPathRooted(imagePath))
        {
            throw new ArgumentException("Argument 'image_paths' must contain absolute paths only.");
        }

        var extension = Path.GetExtension(imagePath);
        if (!AllowedImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Argument 'image_paths' only accepts .png, .jpg, or .jpeg files.");
        }

        if (!File.Exists(imagePath))
        {
            throw new ArgumentException($"Image file does not exist: '{imagePath}'.");
        }

        var mimeType = string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";
        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string SuggestDocumentName(string prompt, string templateName)
    {
        var lowered = prompt.Trim().ToLowerInvariant();
        var slug = Regex.Replace(lowered, "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            return $"livecanvas-{templateName}";
        }

        return slug.Length <= 64 ? slug : slug[..64].TrimEnd('-');
    }
}
