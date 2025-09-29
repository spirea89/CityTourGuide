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
    private const string SystemMessage = """
You are a creative yet trustworthy city tour guide. Base every response strictly on the verified facts supplied by the user. If the prompt says information is missing, acknowledge the gap instead of inventing details. Keep the tone welcoming and vivid while staying factual.
""";
    private const string HistoryPromptTemplate = """
You are a meticulous local historian introducing visitors to {building_name} at {address}. Write the story in {language}.

Work only with the verified facts listed below. If a detail is absent, say so rather than guessing.

FACTS:
{facts}

TASK:
Write a chronological 120–150 word history that highlights the founding date, any name changes, two to three pivotal events, and why the place matters today. Keep the tone warm but precise.
""";
    private const string PersonalitiesPromptTemplate = """
You are a culturally savvy guide explaining the people connected to {building_name} at {address}. Respond in {language}.

Use only the verified facts below. If information about a figure is missing, be transparent about the uncertainty.

FACTS:
{facts}

TASK:
Craft a 110–140 word mini-story that weaves in two to three notable figures with full names, relevant dates, their roles, and one concrete anecdote each.
""";
    private const string ArchitecturePromptTemplate = """
You are an architect talking to curious visitors about {building_name} at {address}. Answer in {language}.

Ground every observation in the verified facts provided. Do not speculate about elements that are not documented.

FACTS:
{facts}

TASK:
Describe the site's architectural style, architect, era, materials, notable façade or interior details, significant alterations, and two street-level features to notice in roughly 120–150 words.
""";
    private const string TodayPromptTemplate = """
You are a practical local host briefing visitors about {building_name} at {address}. Respond in {language}.

Rely only on the verified facts below. If a requested detail is missing, clearly note that it is not documented.

FACTS:
{facts}

TASK:
Summarize in 90–120 words the current purpose or occupants, public access details (such as hours, ticketing, accessibility), photo or etiquette guidance, and one nearby tip.
""";
    private const string KidsPromptTemplate = """
You are a playful storyteller for children aged 6–10 visiting {building_name} at {address}. Tell the tale in {language}.

Stick to the verified facts. Highlight what is known and gently mention when a detail is unknown.

FACTS:
{facts}

TASK:
Tell a cheerful 90–110 word story using simple sentences, fun comparisons or sounds, one exciting fact from the list, no frightening content, and end with a question that invites kids to spot something when they arrive.
""";

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
        var story = await SendResponseAsync(prompt, 600, "story", cancellationToken);

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
            return "No verified facts were provided. Explain to the visitor that trustworthy details for this site could not be confirmed.";
        }

        var lines = facts
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var builder = new StringBuilder();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!line.StartsWith("-"))
            {
                builder.Append("- ");
            }

            builder.Append(line);
            builder.AppendLine();
        }

        var formatted = builder.ToString().TrimEnd();
        return formatted.Length > 0 ? formatted : facts.Trim();
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
        return await SendResponseAsync(prompt, 400, "follow-up answer", cancellationToken);
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

    private async Task<string> SendResponseAsync(
        string prompt,
        int maxOutputTokens,
        string failureContext,
        CancellationToken cancellationToken)
    {
        var key = GetOrThrowApiKey();

        var payload = new
        {
            model = _model,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new { type = "input_text", text = SystemMessage }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = prompt }
                    }
                }
            },
            max_output_tokens = maxOutputTokens
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
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
            var content = TryExtractPrimaryText(doc.RootElement);

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException($"OpenAI response did not contain {failureContext} content.");
            }

            return content.Trim();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to parse OpenAI {Context} response: {Body}", failureContext, responseBody);
            throw new OpenAiResponseParseException(failureContext, responseBody, ex);
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

    private static string? TryExtractPrimaryText(JsonElement root)
    {
        var text = TryExtractFromResponsesApi(root);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("response", out var nestedResponse))
            {
                text = TryExtractFromResponsesApi(nestedResponse);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            if (root.TryGetProperty("data", out var dataElement))
            {
                text = ExtractTextFromElement(dataElement);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.ValueKind == JsonValueKind.Object)
            {
                if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var messageContent))
                {
                    var contentText = ExtractTextFromElement(messageContent);
                    if (!string.IsNullOrWhiteSpace(contentText))
                    {
                        return contentText;
                    }
                }

                if (choice.TryGetProperty("text", out var legacyText) && legacyText.ValueKind == JsonValueKind.String)
                {
                    var textValue = legacyText.GetString();
                    if (!string.IsNullOrWhiteSpace(textValue))
                    {
                        return textValue;
                    }
                }
            }
        }

        return null;
    }

    private static string? TryExtractFromResponsesApi(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                var text = ExtractTextFromElement(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
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

        if (root.TryGetProperty("output_text", out var outputTextElement))
        {
            var text = ExtractTextFromElement(outputTextElement);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? ExtractTextFromElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Object:
            {
                if (element.TryGetProperty("value", out var valueProperty))
                {
                    var valueText = ExtractTextFromElement(valueProperty);
                    if (!string.IsNullOrWhiteSpace(valueText))
                    {
                        return valueText;
                    }
                }

                if (element.TryGetProperty("text", out var textProperty))
                {
                    var text = ExtractTextFromElement(textProperty);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                if (element.TryGetProperty("content", out var nestedContent))
                {
                    var text = ExtractTextFromElement(nestedContent);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                if (element.TryGetProperty("output", out var nestedOutput))
                {
                    var text = ExtractTextFromElement(nestedOutput);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                if (element.TryGetProperty("output_text", out var nestedOutputText))
                {
                    var text = ExtractTextFromElement(nestedOutputText);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("type") || property.NameEquals("role") || property.NameEquals("id"))
                    {
                        continue;
                    }

                    var text = ExtractTextFromElement(property.Value);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                return null;
            }
            case JsonValueKind.Array:
            {
                var builder = new StringBuilder();
                foreach (var item in element.EnumerateArray())
                {
                    var text = ExtractTextFromElement(item);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (builder.Length > 0)
                        {
                            builder.AppendLine();
                        }
                        builder.Append(text.Trim());
                    }
                }

                return builder.Length > 0 ? builder.ToString() : null;
            }
            default:
                return null;
        }
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
