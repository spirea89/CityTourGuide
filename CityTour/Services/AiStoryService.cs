using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CityTour.Models;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.Logging;

namespace CityTour.Services;

public interface IAiStoryService
{
    Task<StoryGenerationResult> GenerateStoryAsync(
        string buildingName,
        string? buildingAddress,
        StoryCategory category,
        string? facts = null,
        string? language = null,
        CancellationToken cancellationToken = default);

    Task<string> AskAddressDetailsAsync(
        string buildingName,
        string? buildingAddress,
        string? currentStory,
        string question,
        CancellationToken cancellationToken = default);

    string BuildStoryPrompt(
        string buildingName,
        string? buildingAddress,
        StoryCategory category,
        string? facts = null,
        string? language = null);
}

public class AiStoryService : IAiStoryService
{
    private const string DefaultModel = "gpt-4o-mini";
    private const string SystemMessage = "You are a creative, historically knowledgeable city tour guide. Craft short stories and responses about buildings that feel authentic, welcoming, and vivid.";
    private const string HistoryPromptTemplate = "You are a meticulous local historian. Using only {facts} about {building_name} at {address}, write a vivid, chronological ~120–150 word history highlighting founding date, name changes, 2–3 pivotal events, and significance; if a detail is missing, write “unknown,” and respond in {language}.";
    private const string PersonalitiesPromptTemplate = "You are a culturally savvy guide. From {facts} on people linked to {building_name} at {address}, craft a ~110–140 word mini-story weaving 2–3 notable figures with full names, dates, roles, and one concrete anecdote each; avoid speculation, note “unknown” when needed, and write in {language}.";
    private const string ArchitecturePromptTemplate = "You are an architect explaining to curious visitors. Based strictly on {facts}, describe {building_name}’s style, architect, era, materials, façade/interior highlights, notable alterations, and 2 street-level details to spot in clear, jargon-light {language} (~120–150 words), using “unknown” where info is missing.";
    private const string TodayPromptTemplate = "You are a practical local host. In {language} and ~90–120 words, summarize {building_name}’s current purpose/occupants, public access (hours, tickets, accessibility), photo/etiquette notes, and one nearby tip; if any item isn’t in {facts}, state “unknown.”";
    private const string KidsPromptTemplate = "You are a playful storyteller for ages 6–10. In {language} and ~90–110 words, tell a cheerful, simple story about {building_name} using easy sentences, fun comparisons or sounds, one cool fact from {facts}, no scary content, and end with a question inviting kids to spot a detail when they visit.";

    private readonly HttpClient _httpClient;
    private readonly ILogger<AiStoryService> _logger;
    private readonly IApiKeyProvider _apiKeyProvider;
    private readonly string _model;
    private string? _apiKey;

    public AiStoryService(HttpClient httpClient, ILogger<AiStoryService> logger, IApiKeyProvider apiKeyProvider)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _logger = logger;
        _apiKeyProvider = apiKeyProvider;
        _model = Preferences.Get("ai.story.model", DefaultModel);
    }

    public async Task<StoryGenerationResult> GenerateStoryAsync(
        string buildingName,
        string? buildingAddress,
        StoryCategory category,
        string? facts = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildingName) && string.IsNullOrWhiteSpace(buildingAddress))
        {
            throw new ArgumentException("A building name or address is required.", nameof(buildingName));
        }

        var prompt = BuildStoryPrompt(buildingName, buildingAddress, category, facts, language);
        var story = await SendChatCompletionAsync(prompt, 0.8, 600, "story", cancellationToken);

        return new StoryGenerationResult(story, prompt);
    }

    public string BuildStoryPrompt(
        string buildingName,
        string? buildingAddress,
        StoryCategory category,
        string? facts = null,
        string? language = null)
    {
        var template = GetPromptTemplate(category);
        var resolvedFacts = ResolveFacts(facts);
        var resolvedName = ResolveBuildingName(buildingName, buildingAddress);
        var resolvedAddress = ResolveAddress(buildingAddress);
        var resolvedLanguage = ResolveLanguage(language);

        var prompt = template
            .Replace("{building_name}", resolvedName, StringComparison.Ordinal)
            .Replace("{address}", resolvedAddress, StringComparison.Ordinal);

        if (template.Contains("{language}", StringComparison.Ordinal))
        {
            prompt = prompt.Replace("{language}", resolvedLanguage, StringComparison.Ordinal);
        }

        if (template.Contains("{facts}", StringComparison.Ordinal))
        {
            prompt = prompt.Replace("{facts}", resolvedFacts, StringComparison.Ordinal);
        }
        else if (!string.IsNullOrWhiteSpace(facts))
        {
            prompt = string.Concat(prompt, "\n\nFacts:\n", resolvedFacts);
        }

        return prompt;
    }

    private static string GetPromptTemplate(StoryCategory category)
    {
        return category switch
        {
            StoryCategory.History => HistoryPromptTemplate,
            StoryCategory.Personalities => PersonalitiesPromptTemplate,
            StoryCategory.Architecture => ArchitecturePromptTemplate,
            StoryCategory.Today => TodayPromptTemplate,
            StoryCategory.Kids => KidsPromptTemplate,
            _ => HistoryPromptTemplate
        };
    }

    private static string ResolveFacts(string? facts)
    {
        return string.IsNullOrWhiteSpace(facts) ? "unknown" : facts.Trim();
    }

    private static string ResolveBuildingName(string buildingName, string? buildingAddress)
    {
        if (!string.IsNullOrWhiteSpace(buildingName))
        {
            return buildingName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(buildingAddress))
        {
            return buildingAddress.Trim();
        }

        return "unknown building";
    }

    private static string ResolveAddress(string? buildingAddress)
    {
        return string.IsNullOrWhiteSpace(buildingAddress) ? "unknown" : buildingAddress.Trim();
    }

    private static string ResolveLanguage(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            return language.Trim();
        }

        var culture = CultureInfo.CurrentUICulture ?? CultureInfo.CurrentCulture;
        var text = culture.EnglishName;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = culture.DisplayName;
        }

        return string.IsNullOrWhiteSpace(text) ? "English" : text;
    }

    public async Task<string> AskAddressDetailsAsync(
        string buildingName,
        string? buildingAddress,
        string? currentStory,
        string question,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildingName) && string.IsNullOrWhiteSpace(buildingAddress))
        {
            throw new ArgumentException("A building name or address is required.", nameof(buildingName));
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("A question is required.", nameof(question));
        }

        var prompt = BuildFollowUpPrompt(buildingName, buildingAddress, currentStory, question);
        return await SendChatCompletionAsync(prompt, 0.7, 400, "follow-up answer", cancellationToken);
    }

    private string BuildFollowUpPrompt(
        string buildingName,
        string? buildingAddress,
        string? currentStory,
        string question)
    {
        var promptBuilder = new StringBuilder();
        var trimmedQuestion = question.Trim();

        if (!string.IsNullOrWhiteSpace(buildingAddress))
        {
            promptBuilder.AppendLine($"A visitor is curious about the building at the exact street address \"{buildingAddress}\".");
            promptBuilder.AppendLine("Use reliable historical knowledge tied to that location whenever possible.");
        }
        else
        {
            promptBuilder.AppendLine($"A visitor is curious about the building known as \"{buildingName}\".");
        }

        var trimmedStory = string.IsNullOrWhiteSpace(currentStory) ? null : currentStory.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedStory))
        {
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Here is the story that was previously shared about this place:");
            promptBuilder.AppendLine(trimmedStory);
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Answer the visitor's follow-up question with historically grounded, welcoming detail.");
        promptBuilder.AppendLine("If you are unsure about any detail, acknowledge the uncertainty instead of inventing facts.");
        promptBuilder.AppendLine("Keep the response concise—no more than two short paragraphs.");
        promptBuilder.Append("Question: ").Append(trimmedQuestion);

        return promptBuilder.ToString().Trim();
    }

    private async Task<string> SendChatCompletionAsync(
        string prompt,
        double temperature,
        int maxTokens,
        string failureContext,
        CancellationToken cancellationToken)
    {
        var key = GetOrThrowApiKey();

        var payload = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = SystemMessage },
                new { role = "user", content = prompt }
            },
            temperature,
            max_tokens = maxTokens
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
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException($"OpenAI response did not contain {failureContext} content.");
            }

            return content.Trim();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to parse OpenAI {Context} response: {Body}", failureContext, responseBody);
            throw new InvalidOperationException($"Failed to parse the {failureContext} response from OpenAI.", ex);
        }
    }

    private string GetOrThrowApiKey()
    {
        var key = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("No OpenAI API key configured. Set the OPENAI_API_KEY environment variable or save it in Preferences under 'ai.story.apikey'.");
        }

        return key;
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            return _apiKey;
        }

        var key = _apiKeyProvider.OpenAiApiKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            _apiKey = key;
            return key;
        }

        key = Preferences.Get("ai.story.apikey", string.Empty);
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
