using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.Logging;

namespace CityTour.Services;

public interface IAiStoryService
{
    Task<string> GenerateStoryAsync(string buildingName, string? buildingAddress, CancellationToken cancellationToken = default);
}

public class AiStoryService : IAiStoryService
{
    private const string DefaultModel = "gpt-4o-mini";

    private readonly HttpClient _httpClient;
    private readonly ILogger<AiStoryService> _logger;
    private readonly string _model;
    private string? _apiKey;

    public AiStoryService(HttpClient httpClient, ILogger<AiStoryService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _logger = logger;
        _model = Preferences.Get("ai.story.model", DefaultModel);
    }

    public async Task<string> GenerateStoryAsync(string buildingName, string? buildingAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildingName))
        {
            throw new ArgumentException("Building name is required.", nameof(buildingName));
        }

        var key = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("No OpenAI API key configured. Set the OPENAI_API_KEY environment variable or save it in Preferences under 'ai.story.apikey'.");
        }

        var prompt = BuildPrompt(buildingName, buildingAddress);

        var payload = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = "You are a creative, historically knowledgeable city tour guide. Craft short stories about buildings that feel authentic, welcoming, and vivid." },
                new { role = "user", content = prompt }
            },
            temperature = 0.8,
            max_tokens = 600
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = TryExtractErrorMessage(responseBody);
            var friendlyMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? $"OpenAI API error ({(int)response.StatusCode})."
                : $"OpenAI API error ({(int)response.StatusCode}): {errorMessage}";

            _logger.LogError("OpenAI API returned {Status}: {Body}", response.StatusCode, responseBody);
            throw new InvalidOperationException(friendlyMessage);
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var story = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(story))
            {
                throw new InvalidOperationException("OpenAI response did not contain story content.");
            }

            return story.Trim();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to parse OpenAI response: {Body}", responseBody);
            throw new InvalidOperationException("Failed to parse the story response from OpenAI.", ex);
        }
    }

    private string BuildPrompt(string buildingName, string? buildingAddress)
    {
        var addressPart = string.IsNullOrWhiteSpace(buildingAddress)
            ? string.Empty
            : $" located at {buildingAddress}";

        var prompt = $"Write an engaging four-paragraph story about the building called \"{buildingName}\"{addressPart}. " +
                     "Blend historical facts, cultural impact, and vivid sensory details so tourists feel immersed. " +
                     "Keep the tone warm and welcoming, and aim for 4-6 sentences per paragraph.";

        return prompt;
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            return _apiKey;
        }

        var key = Preferences.Get("ai.story.apikey", string.Empty);
        if (!string.IsNullOrWhiteSpace(key))
        {
            _apiKey = key;
            return key;
        }

        key = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        _apiKey = key;
        return key;
    }

    private static string? TryExtractErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
                {
                    return message.GetString();
                }

                var text = error.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        catch (JsonException)
        {
            // Ignore parse failures and fall back to the raw message.
        }

        return null;
    }
}
