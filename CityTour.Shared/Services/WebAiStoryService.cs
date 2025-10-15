using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CityTour.Core.Services;
using CityTour.Models;
using Microsoft.Extensions.Logging;

namespace CityTour.Services;

public class WebAiStoryService : IAiStoryService
{
    private const string DefaultModel = "gpt-4o";
    private const string SystemMessage = "You are a helpful assistant that creates engaging stories about buildings and landmarks for tourists and visitors.";
    private const string HistoryPromptTemplate = "You are a comprehensive local historian and cultural guide. Based on the available information about {building_name} at {address}: {facts}\n\nWrite a rich, engaging ~130–160 word historical narrative that weaves together multiple aspects: the building's founding and historical timeline, notable people who lived or worked here, and distinctive architectural features that visitors can observe. Include specific dates, events, and cultural significance. If you have information about historical figures, weave their stories into the narrative. If architectural details are available, explain the style, construction period, and what makes this building visually interesting. If information is limited, focus on the broader historical context and what makes this place significant to visitors. Use welcoming, informative language that brings the place to life.";
    private const string TodayPromptTemplate = "You are a practical local host helping visitors explore {building_name} at {address}. Here's what we know: {facts}\n\nIn ~90–120 words, provide useful visitor information. If you have current details about purpose, access, or visiting tips, share those. If information is limited, offer general guidance about approaching and appreciating the building, suggest what visitors might look for, and provide contextual tips for exploring the surrounding area.";
    private const string KidsPromptTemplate = "You are a fun storyteller for 7-year-old children visiting {building_name} at {address}. Here's what we know: {facts}\n\nTell a magical ~100–120 word story using simple, exciting language perfect for a 7-year-old. Use comparisons they'll understand (like 'as tall as a giant' or 'as old as your great-grandma'). If you have historical details, turn them into an adventure story with interesting characters. If information is limited, create an imaginative tale about what exciting things might have happened here long ago. End with a fun question that encourages them to use their imagination when they visit. Make it sparkle with wonder and discovery!";

    private readonly HttpClient _httpClient;
    private readonly ILogger<WebAiStoryService> _logger;
    private readonly IApiKeyProvider _apiKeyProvider;
    private readonly IBuildingContextService _contextService;
    private readonly IFactCheckService _factCheckService;
    private string _model;

    public WebAiStoryService(HttpClient httpClient, ILogger<WebAiStoryService> logger, IApiKeyProvider apiKeyProvider, IBuildingContextService contextService, IFactCheckService factCheckService)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _logger = logger;
        _apiKeyProvider = apiKeyProvider;
        _contextService = contextService;
        _factCheckService = factCheckService;
        _model = DefaultModel;
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

        // Get enhanced building context
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
        var maxTokens = IsGpt5Model(_model) ? 1500 : 600;
        var story = await SendCompletionAsync(prompt, 0.8, maxTokens, "story", cancellationToken);

        return new StoryGenerationResult(story, prompt);
    }

    public async Task<StoryFactCheckResult> GenerateStoryWithFactCheckAsync(
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

        // Generate the story first
        var storyResult = await GenerateStoryAsync(buildingName, buildingAddress, category, facts, language, latitude, longitude, cancellationToken);

        // Then fact-check it
        FactCheckSummary factCheck;
        try
        {
            _logger.LogDebug("Starting fact-check for story about {BuildingName}", buildingName);
            factCheck = await _factCheckService.VerifyStoryAsync(storyResult.Story, buildingName, buildingAddress, cancellationToken);
            _logger.LogDebug("Fact-check completed for {BuildingName}", buildingName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fact-check failed for {BuildingName}, returning story without fact-check", buildingName);
            
            // Return a basic fact-check result if the service fails
            factCheck = new FactCheckSummary(
                new List<FactCheckItem>(),
                new List<FactCheckItem> 
                { 
                    new("Fact-checking service unavailable", FactCheckStatus.Uncertain, $"Error: {ex.Message}", "System") 
                },
                new List<FactCheckItem>(),
                false,
                "Fact-checking could not be completed due to technical issues.");
        }

        return new StoryFactCheckResult(storyResult.Story, storyResult.Prompt, factCheck);
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

    private static bool IsGpt5Model(string model)
    {
        return model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
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
        var maxTokens = IsGpt5Model(_model) ? 800 : 400;
        return await SendCompletionAsync(prompt, 0.7, maxTokens, "follow-up answer", cancellationToken);
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
            throw exception;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            _logger.LogDebug("OpenAI response received, attempting to extract content from: {ResponseKeys}", 
                string.Join(", ", root.EnumerateObject().Select(p => p.Name)));

            var completionContent = OpenAiResponseContentExtractor.TryExtractCompletionContent(root);
            if (!string.IsNullOrWhiteSpace(completionContent))
            {
                _logger.LogDebug("Successfully extracted content of length: {Length}", completionContent.Length);
                return completionContent.Trim();
            }

            _logger.LogWarning("Failed to extract content from OpenAI response. Response structure: {Response}", 
                responseBody.Length > 1000 ? responseBody.Substring(0, 1000) + "..." : responseBody);

            if (TryBuildIncompleteResponseMessage(root, failureContext, out var friendlyMessage))
            {
                _logger.LogWarning("OpenAI returned incomplete {Context} response: {Message}", failureContext, friendlyMessage);
                var exception = new InvalidOperationException(friendlyMessage);
                throw exception;
            }

            _logger.LogError("OpenAI response did not contain {Context} content: {Body}", failureContext, responseBody);
            var missingContentException = new InvalidOperationException($"OpenAI response did not contain {failureContext} content.");
            throw missingContentException;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse OpenAI {Context} response: {Body}", failureContext, responseBody);
            var parseException = new InvalidOperationException($"Failed to parse the {failureContext} response from OpenAI.", ex);
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

    private string GetOrThrowApiKey()
    {
        var key = _apiKeyProvider.OpenAiApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("No OpenAI API key configured. Please set the OPENAI_API_KEY environment variable.");
        }

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
