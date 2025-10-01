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
    private const string DefaultModel = "gpt-5";
    private const string ModelPreferenceKey = "ai.story.model";
    private const string SystemMessage = AiStorySystemPrompts.Default;
    internal const string RawResponseDataKey = "RawOpenAiResponse";
    private const string HistoryPromptTemplate = "You are a meticulous local historian. Using only facts about {address}, write a vivid, chronological ~120–150 word history highlighting founding date, name changes, 2–3 pivotal events, and significance.";
    private const string PersonalitiesPromptTemplate = "You are a culturally savvy guide. From facts on people linked to {address}, craft a ~110–140 word mini-story weaving 2–3 notable figures with full names, dates, roles, and one concrete anecdote each; avoid speculation.";
    private const string ArchitecturePromptTemplate = "You are an architect explaining to curious visitors. Based strictly on facts, describe {address}’s style, architect, era, materials, façade/interior highlights, notable alterations, and 2 street-level details to spot in clear (~120–150 words).";
    private const string TodayPromptTemplate = "You are a practical local host. In ~90–120 words, summarize {address}’s current purpose/occupants, public access (hours, tickets, accessibility), photo/etiquette notes, and one nearby tip; if any item isn’t in facts.” ";
    private const string KidsPromptTemplate = "You are a playful storyteller for ages 6–10. In ~90–110 words, tell a cheerful, simple story about {address} using easy sentences, fun comparisons or sounds, one cool fact from facts, no scary content, and end with a question inviting kids to spot a detail when they visit.";

    private readonly HttpClient _httpClient;
    private readonly ILogger<AiStoryService> _logger;
    private readonly IApiKeyProvider _apiKeyProvider;
    private string _model;
    private string? _apiKey;

    public AiStoryService(HttpClient httpClient, ILogger<AiStoryService> logger, IApiKeyProvider apiKeyProvider)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _logger = logger;
        _apiKeyProvider = apiKeyProvider;
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

        var payload = AiStoryPayloadFactory.Create(_model, prompt, temperature, maxTokens, SystemMessage);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var content = new StringContent(payload.ToJsonString(), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = Encoding.UTF8.WebName
        };

        request.Content = content;

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

            var content = TryExtractCompletionContent(root);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content.Trim();
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

    private static string? TryExtractCompletionContent(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                var text = TryExtractTextFromChoice(choice);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            var text = ExtractTextFromElement(output);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
        {
            var text = ExtractTextFromElement(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        if (root.TryGetProperty("content", out var contentElement))
        {
            var text = ExtractTextFromElement(contentElement);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? TryExtractTextFromChoice(JsonElement choice)
    {
        if (choice.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
        {
            var messageText = ExtractTextFromElement(message);
            if (!string.IsNullOrWhiteSpace(messageText))
            {
                return messageText;
            }
        }

        if (choice.TryGetProperty("text", out var textElement))
        {
            return textElement.ValueKind == JsonValueKind.String ? textElement.GetString() : ExtractTextFromElement(textElement);
        }

        if (choice.TryGetProperty("content", out var contentElement))
        {
            var text = ExtractTextFromElement(contentElement);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? ExtractTextFromElement(JsonElement element)
    {
        var builder = new StringBuilder();
        AppendTextFromElement(element, builder);
        return builder.Length > 0 ? builder.ToString() : null;
    }

    private static void AppendTextFromElement(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AppendText(builder, element.GetString());
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AppendTextFromElement(item, builder);
                }

                break;
            case JsonValueKind.Object:
                if (element.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
                {
                    var type = typeElement.GetString();
                    if (string.Equals(type, "reasoning", StringComparison.OrdinalIgnoreCase))
                    {
                        if (element.TryGetProperty("summary", out var summary))
                        {
                            AppendTextFromElement(summary, builder);
                        }

                        break;
                    }

                    if (string.Equals(type, "output_text", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        if (element.TryGetProperty("text", out var typedText))
                        {
                            if (typedText.ValueKind == JsonValueKind.String)
                            {
                                AppendText(builder, typedText.GetString());
                            }
                            else
                            {
                                AppendTextFromElement(typedText, builder);
                            }
                        }

                        break;
                    }
                }

                if (element.TryGetProperty("text", out var text))
                {
                    if (text.ValueKind == JsonValueKind.String)
                    {
                        AppendText(builder, text.GetString());
                    }
                    else
                    {
                        AppendTextFromElement(text, builder);
                    }
                }

                if (element.TryGetProperty("content", out var content))
                {
                    AppendTextFromElement(content, builder);
                }

                if (element.TryGetProperty("value", out var value))
                {
                    AppendTextFromElement(value, builder);
                }

                break;
        }
    }

    private static void AppendText(StringBuilder builder, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine().AppendLine();
        }

        builder.Append(text.Trim());
    }

    internal static JsonObject CreateChatCompletionPayload(string model, string prompt, double temperature, int maxTokens)
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
