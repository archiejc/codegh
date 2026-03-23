using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LiveCanvas.AgentHost.Copilot;

public sealed class OpenAiCompatibleCopilotModelClient(HttpClient httpClient, CopilotOptions options) : ICopilotModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> CreateReferenceBriefJsonAsync(
        string prompt,
        IReadOnlyList<string> imageDataUrls,
        CancellationToken cancellationToken = default)
    {
        options.EnsureConfigured("copilot_plan");

        var endpoint = $"{options.BaseUrl!.TrimEnd('/')}/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey!);

        var contentItems = new List<object>
        {
            new
            {
                type = "text",
                text = """
                       You are a building massing extraction assistant.
                       Return exactly one JSON object that can be deserialized into the ReferenceBrief schema:
                       {
                         "buildingType": string,
                         "siteContext": string,
                         "massingStrategy": string,
                         "approxDimensions": { "width": number, "depth": number, "height": number },
                         "leveling": { "podiumHeight": number|null, "towerHeight": number|null, "stepCount": number|null },
                         "transformHints": { "rotationDegrees": number|null, "taperRatio": number|null, "offsetPattern": string|null },
                         "styleHints": { "color": [number, number, number]|null, "silhouette": string|null },
                         "confidence": number,
                         "assumptions": string[]
                       }
                       Do not include markdown fences.
                       """
            },
            new
            {
                type = "text",
                text = $"User prompt: {prompt}"
            }
        };

        foreach (var dataUrl in imageDataUrls)
        {
            contentItems.Add(new
            {
                type = "image_url",
                image_url = new { url = dataUrl }
            });
        }

        var payload = new
        {
            model = options.Model,
            temperature = 0.1,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = contentItems
                }
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ArgumentException($"Copilot provider returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}. Body: {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        var content = ExtractMessageContent(document.RootElement);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Copilot provider returned an empty completion content.");
        }

        return StripMarkdownFences(content.Trim());
    }

    private static string ExtractMessageContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new ArgumentException("Copilot provider response did not include choices.");
        }

        var message = choices[0].GetProperty("message");
        var content = message.GetProperty("content");

        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(
                string.Empty,
                content.EnumerateArray()
                    .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "text")
                    .Select(item => item.TryGetProperty("text", out var text) ? text.GetString() : null)
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            _ => throw new ArgumentException("Copilot provider message content has unsupported shape.")
        };
    }

    private static string StripMarkdownFences(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var lines = text.Split('\n').ToList();
        if (lines.Count == 0)
        {
            return text;
        }

        if (lines[0].StartsWith("```", StringComparison.Ordinal))
        {
            lines.RemoveAt(0);
        }

        if (lines.Count > 0 && lines[^1].Trim() == "```")
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines).Trim();
    }
}
