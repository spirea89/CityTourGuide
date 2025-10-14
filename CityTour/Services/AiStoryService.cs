using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CityTour.Core.Services;
using CityTour.Models;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.Logging;

namespace CityTour.Services;

public interface IAiStoryService
{
    string CurrentModel { get; }

    Task<StoryGenerationResult> GenerateStoryAsync(
        string buildingName,
        string? buildingAddress,
        StoryCategory category,
        string? facts = null,
        string? language = null,
        double? latitude = null,
        double? longitude = null,
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

    void SetModel(string model);
}

public class AiStoryService : IAiStoryService
{
    private const string DefaultModel = "gpt-4o";
    private const string ModelPreferenceKey = "ai.story.model";
    private const string SystemMessage = AiStorySystemPrompts.Default;
    internal const string RawResponseDataKey = "RawOpenAiResponse";
    private const string HistoryPromptTemplate = "You are a meticulous local historian. Based on the available information about {building_name} at {address}: {facts}\n\nWrite a vivid, engaging ~120–150 word historical narrative. If you have specific historical details, create a chronological story highlighting founding dates, significant events, and cultural importance. If information is limited, focus on the broader historical context of the location and what makes this place potentially significant to visitors. Use welcoming, informative language that brings the place to life.";
    private const string PersonalitiesPromptTemplate = "You are a culturally savvy guide. Based on what we know about {building_name} at {address}: {facts}\n\nCraft an engaging ~110–140 word story about notable people connected to this place. If you have specific information about historical figures, weave their stories with dates, roles, and interesting anecdotes. If details are limited, discuss the types of people who might have lived, worked, or gathered here throughout history, connecting to the broader social fabric of the area.";
    private const string ArchitecturePromptTemplate = "You are an architect explaining to curious visitors. Looking at {building_name} at {address}: {facts}\n\nDescribe the architectural story in ~120–150 words. If you have specific details about style, architect, or construction, highlight those features visitors can observe. If information is limited, guide visitors on what architectural elements to look for based on the building's apparent age, location, and context within the neighborhood. Help them appreciate the visual details they can spot.";
    private const string TodayPromptTemplate = "You are a practical local host helping visitors explore {building_name} at {address}. Here's what we know: {facts}\n\nIn ~90–120 words, provide useful visitor information. If you have current details about purpose, access, or visiting tips, share those. If information is limited, offer general guidance about approaching and appreciating the building, suggest what visitors might look for, and provide contextual tips for exploring the surrounding area.";
    private const string KidsPromptTemplate = "You are a fun storyteller for kids aged 6–10 visiting {building_name} at {address}. Here's what we know: {facts}\n\nTell a cheerful ~90–110 word story using simple sentences and fun comparisons. If you have interesting historical details, turn them into an engaging tale. If information is limited, create an imaginative story about what adventures might have happened here long ago, ending with a question that encourages kids to use their imagination when they visit.";

    private readonly HttpClient _httpClient;
    private readonly ILogger<AiStoryService> _logger;
    private readonly IApiKeyProvider _apiKeyProvider;
    private readonly IBuildingContextService _contextService;
    private string _model;
    private string? _apiKey;

    public AiStoryService(HttpClient httpClient, ILogger<AiStoryService> logger, IApiKeyProvider apiKeyProvider, IBuildingContextService contextService)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _logger = logger;
        _apiKeyProvider = apiKeyProvider;
        _contextService = contextService;
        var savedModel = Preferences.Get(ModelPreferenceKey, DefaultModel);
        _model = string.IsNullOrWhiteSpace(savedModel) ? DefaultModel : savedModel.Trim();
    }

    public string CurrentModel => _model;

    public void SetModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("A model name is required.", nameof(model));
        }

        var trimmed = model.Trim();
        if (string.Equals(_model, trimmed, StringComparison.Ordinal))
        {
            return;
        }

        _model = trimmed;
        Preferences.Set(ModelPreferenceKey, _model);
    }

    public async Task<StoryGenerationResult> GenerateStoryAsync(
        string buildingName,
        string? buildingAddress,
        StoryCategory category,
        string? facts = null,
        string? language = null,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildingName) && string.IsNullOrWhiteSpace(buildingAddress))
        {
            throw new ArgumentException("A building name or address is required.", nameof(buildingName));
        }

        // Get enhanced building context by combining existing facts with Wikipedia information
        BuildingContextResult contextResult;
        try
        {
            _logger.LogDebug("Gathering enhanced context for {BuildingName} at {Address}", buildingName, buildingAddress);
            contextResult = await _contextService.GetEnhancedContextAsync(
                buildingName, 
                buildingAddress, 
                facts, 
                latitude: latitude,
                longitude: longitude, 
                cancellationToken);
            
            _logger.LogDebug("Enhanced context gathered. Has Wikipedia info: {HasWikipedia}", contextResult.HasWikipediaInfo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to gather enhanced context for {BuildingName}, using existing facts", buildingName);
            contextResult = new BuildingContextResult(facts ?? "Limited information available", null, null, false);
        }

        var prompt = BuildStoryPrompt(buildingName, buildingAddress, category, contextResult.EnhancedFacts, language);
        var story = await SendCompletionAsync(prompt, 0.8, 600, "story", cancellationToken);

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

        return template
            .Replace("{facts}", resolvedFacts, StringComparison.Ordinal)
            .Replace("{building_name}", resolvedName, StringComparison.Ordinal)
            .Replace("{address}", resolvedAddress, StringComparison.Ordinal)
            .Replace("{language}", resolvedLanguage, StringComparison.Ordinal);
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
        if (string.IsNullOrWhiteSpace(facts))
        {
            return "Limited information is available about this location.";
        }
        
        var trimmed = facts.Trim();
        return trimmed.Length > 10 ? trimmed : "Basic location information available.";
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
        return string.IsNullOrWhiteSpace(buildingAddress) ? "this location" : buildingAddress.Trim();
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
        return await SendCompletionAsync(prompt, 0.7, 400, "follow-up answer", cancellationToken);
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

    private async Task<string> SendCompletionAsync(
        string prompt,
        double temperature,
        int maxTokens,
        string failureContext,
        CancellationToken cancellationToken)
    {
        var key = GetOrThrowApiKey();

        var payload = AiStoryPayloadFactory.Create(_model, prompt, temperature, maxTokens, SystemMessage);

        var endpoint = AiStoryPayloadFactory.UsesResponsesEndpoint(_model)
            ? "https://api.openai.com/v1/responses"
            : "https://api.openai.com/v1/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var jsonContent = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        request.Content = jsonContent;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = TryExtractErrorMessage(responseBody);
            var friendlyMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? $"OpenAI API error ({(int)response.StatusCode})."
                : $"OpenAI API error ({(int)response.StatusCode}): {errorMessage}";

            _logger.LogError("OpenAI API returned {Status}: {Body}", response.StatusCode, responseBody);

            var exception = new InvalidOperationException(friendlyMessage);
            exception.Data[RawResponseDataKey] = responseBody;
            throw exception;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var completionContent = OpenAiResponseContentExtractor.TryExtractCompletionContent(root);
            if (!string.IsNullOrWhiteSpace(completionContent))
            {
                return completionContent.Trim();
            }

            if (TryBuildIncompleteResponseMessage(root, failureContext, out var friendlyMessage))
            {
                _logger.LogWarning("OpenAI returned incomplete {Context} response: {Message}", failureContext, friendlyMessage);
                var exception = new InvalidOperationException(friendlyMessage);
                exception.Data[RawResponseDataKey] = responseBody;
                throw exception;
            }

            _logger.LogError("OpenAI response did not contain {Context} content: {Body}", failureContext, responseBody);
            var missingContentException = new InvalidOperationException($"OpenAI response did not contain {failureContext} content.");
            missingContentException.Data[RawResponseDataKey] = responseBody;
            throw missingContentException;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse OpenAI {Context} response: {Body}", failureContext, responseBody);
            var parseException = new InvalidOperationException($"Failed to parse the {failureContext} response from OpenAI.", ex);
            parseException.Data[RawResponseDataKey] = responseBody;
            throw parseException;
        }
    }

    private static bool TryBuildIncompleteResponseMessage(JsonElement root, string failureContext, out string message)
    {
        message = string.Empty;

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.String
            && string.Equals(status.GetString(), "incomplete", StringComparison.OrdinalIgnoreCase))
        {
            string? reason = null;
            if (root.TryGetProperty("incomplete_details", out var details)
                && details.ValueKind == JsonValueKind.Object
                && details.TryGetProperty("reason", out var reasonElement)
                && reasonElement.ValueKind == JsonValueKind.String)
            {
                reason = reasonElement.GetString();
            }

            message = string.IsNullOrWhiteSpace(reason)
                ? $"OpenAI stopped generating the {failureContext} before it was finished. Try again or adjust your request."
                : $"OpenAI stopped generating the {failureContext} early ({reason}). Try again or adjust your request.";

            return true;
        }

        return false;
    }

    internal static JsonObject CreateCompletionPayload(string model, string prompt, double temperature, int maxTokens)
        => AiStoryPayloadFactory.Create(model, prompt, temperature, maxTokens, SystemMessage);

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

        key = EnvironmentSecrets.TryGetValue("OPENAI_API_KEY") ?? string.Empty;
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
