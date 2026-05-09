using System.Net.Http.Json;
using System.Text.Json;

namespace StarXelem.VisualJudge;

public record ComparisonResult(
    bool IsCompliant,
    double Score,
    GapRecord[] Gaps,
    string Summary,
    bool IsSkipped)
{
    public static ComparisonResult Skipped(string reason) => new(false, 0.0, [], reason, true);
}

public record GapRecord(
    string Category,
    string Description,
    string Severity);

public static class OllamaVisualJudge
{
    private const string DefaultEndpoint = "http://localhost:11434";
    private const string ModelName = "llava:7b";

    public static async Task<ComparisonResult> CompareAsync(
        string actualImagePath,
        string referenceImagePath,
        string pageName,
        string? endpoint = null)
    {
        var baseUri = (endpoint ?? DefaultEndpoint).TrimEnd('/');

        // Check connectivity first
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            var pingResponse = await client.GetAsync($"{baseUri}/api/tags");
            if (!pingResponse.IsSuccessStatusCode)
                return ComparisonResult.Skipped($"Ollama not reachable at {baseUri}");
        }
        catch
        {
            return ComparisonResult.Skipped($"Cannot connect to Ollama at {baseUri}");
        }

        string actualBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(actualImagePath));
        string referenceBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(referenceImagePath));

        string prompt = $"""
            You are an expert in GUI validation. Compare two screenshots:
            - Image 1 (reference): the expected design
            - Image 2 (actual): what the application currently renders

            Page name: "{pageName}"

            Return ONLY a JSON object with this exact structure, no markdown, no explanation.
            The JSON should have is_compliant (bool), score (0-1), gaps (array of objects with category/description/severity fields), and summary (string).
            """;

        var request = new
        {
            model = ModelName,
            prompt,
            images = new[] { referenceBase64, actualBase64 },
            stream = false
        };

        var jsonBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request);
        using var content = new System.Net.Http.ByteArrayContent(jsonBytes) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") } };
        var response = await client.PostAsync($"{baseUri}/api/generate", content);

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync();
            return ComparisonResult.Skipped($"Ollama returned {(int)response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (responseJson.ValueKind == JsonValueKind.Undefined)
            return ComparisonResult.Skipped("Empty response from Ollama");

        string responseText = ParseResponseText(responseJson);
        return ParseVerdict(responseText, pageName);
    }

    static string ParseResponseText(JsonElement json)
    {
        if (json.TryGetProperty("response", out var respProp))
            return respProp.GetString() ?? string.Empty;
        return json.GetRawText();
    }

    static ComparisonResult ParseVerdict(string responseText, string pageName)
    {
        // Try to extract JSON from the response (LLaVA may wrap it in markdown)
        string cleaned = responseText.Trim();

        // Strip markdown code blocks if present
        if (cleaned.StartsWith("```"))
        {
            int firstNewline = cleaned.IndexOf('\n');
            cleaned = firstNewline > 0 ? cleaned[(firstNewline + 1)..] : cleaned;
            int lastBlock = cleaned.LastIndexOf("```");
            if (lastBlock > 0)
                cleaned = cleaned[..lastBlock];
        }

        // Find JSON object
        int jsonStart = cleaned.IndexOf('{');
        int jsonEnd = cleaned.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart)
            return ComparisonResult.Skipped($"Could not parse JSON from LLaVA response for {pageName}");

        string jsonStr = cleaned[jsonStart..(jsonEnd + 1)];

        try
        {
            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;

            bool isCompliant = root.TryGetProperty("is_compliant", out var ic) && ic.GetBoolean();
            double score = root.TryGetProperty("score", out var sc) ? sc.GetDouble() : 0.0;
            string summary = root.TryGetProperty("summary", out var sm) ? sm.GetString() ?? "" : "";

            GapRecord[] gaps = [];
            if (root.TryGetProperty("gaps", out var gapsProp) && gapsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var gap in gapsProp.EnumerateArray())
                {
                    string cat = gap.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                    string desc = gap.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    string sev = gap.TryGetProperty("severity", out var s) ? s.GetString() ?? "minor" : "minor";
                    gaps = gaps.Append(new GapRecord(cat, desc, sev)).ToArray();
                }
            }

            return new ComparisonResult(isCompliant, score, gaps, summary, false);
        }
        catch (JsonException ex)
        {
            return ComparisonResult.Skipped($"JSON parse error: {ex.Message}");
        }
    }
}

