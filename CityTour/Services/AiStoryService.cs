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
        StoryCategory category);
}

public class AiStoryService : IAiStoryService
{
    private const string DefaultModel = "gpt-4o-mini";
    private const string SystemMessage = "You are a creative, historically knowledgeable city tour guide. Craft short stories and responses about buildings that feel authentic, welcoming, and vivid.";

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
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildingName) && string.IsNullOrWhiteSpace(buildingAddress))
        {
            throw new ArgumentException("A building name or address is required.", nameof(buildingName));
        }

        var prompt = BuildStoryPrompt(buildingName, buildingAddress, category);
        var story = await SendChatCompletionAsync(prompt, 0.8, 600, "story", cancellationToken);

        return new StoryGenerationResult(story, prompt);
    }

    public string BuildStoryPrompt(string buildingName, string? buildingAddress, StoryCategory category)
    {
        if (category == StoryCategory.History)
        {
            var address = string.IsNullOrWhiteSpace(buildingAddress) ? buildingName : buildingAddress;
            address ??= string.Empty;
            return $"Tell me the history of the building found at the address: '{address}'.";
        }

        var (focusInstruction, toneInstruction, structureInstruction) = category switch
        {
            StoryCategory.History => (
                "Concentrate on the building's historical background, important events, and the evolution of its role in the city.",
                "Keep the tone warm and welcoming while weaving in vivid sensory details rooted in real history.",
                "Aim for four paragraphs with 4-6 sentences each."
            ),
            StoryCategory.Personalities => (
                "Highlight the notable people linked to the building and share the human stories that connect them to this place.",
                "Keep the tone warm, welcoming, and conversational, as if guiding visitors through personal anecdotes.",
                "Aim for four paragraphs with 4-6 sentences each."
            ),
            StoryCategory.Architecture => (
                "Focus on the building's architectural style, materials, design innovations, and what it feels like to experience the space.",
                "Use descriptive language that helps visitors visualize textures, shapes, and craftsmanship while staying inviting.",
                "Aim for four paragraphs with 4-6 sentences each."
            ),
            StoryCategory.Kids => (
                "Explain the building's story in friendly, easy-to-understand language suited for children around ages 8-12, and include what makes it exciting or special to them.",
                "Keep the tone playful and encouraging, add two or three fun facts or imaginative comparisons, and avoid complex vocabulary.",
                "Write three short paragraphs with 3-4 sentences each."
            ),
            _ => (
                "Blend historical facts, cultural impact, and vivid sensory details so tourists feel immersed.",
                "Keep the tone warm and welcoming.",
                "Aim for four paragraphs with 4-6 sentences each."
            )
        };

        var promptBuilder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(buildingAddress))
        {
            promptBuilder.Append($"Write an engaging story about the building located at the exact street address \"{buildingAddress}\". ");
            promptBuilder.Append("Base the narrative on everything historically connected to that address, no matter the building's past or present purpose. ");
            promptBuilder.Append("Do not include modern marketing or attraction names for the site unless they are historically essential; keep the focus on the street address as the visitor's reference point. ");
            promptBuilder.Append("Use that precise street and number as the anchor when recalling earlier names, occupants, or notable events linked to the site, and mention them when relevant. ");
        }
        else
        {
            promptBuilder.Append($"Write an engaging story about the building called \"{buildingName}\". ");
            promptBuilder.Append("Include meaningful context about how the site has been used or renamed over time whenever possible. ");
        }

        promptBuilder.Append(focusInstruction).Append(' ');
        promptBuilder.Append(toneInstruction).Append(' ');
        promptBuilder.Append(structureInstruction).Append(' ');
        promptBuilder.Append("If any details are uncertain, acknowledge the uncertainty instead of inventing facts.");

        return promptBuilder.ToString().Trim();
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
