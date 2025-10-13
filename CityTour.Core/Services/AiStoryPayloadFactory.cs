using System;
using System.Text.Json.Nodes;

namespace CityTour.Core.Services;

public static class AiStoryPayloadFactory
{
    public static JsonObject Create(
        string model,
        string prompt,
        double temperature,
        int maxTokens,
        string systemMessage = AiStorySystemPrompts.Default)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("A model name is required.", nameof(model));
        }

        if (prompt is null)
        {
            throw new ArgumentNullException(nameof(prompt));
        }

        if (string.IsNullOrWhiteSpace(systemMessage))
        {
            throw new ArgumentException("A system message is required.", nameof(systemMessage));
        }

        if (maxTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "The maximum token count must be positive.");
        }

        var payload = new JsonObject
        {
            ["model"] = model
        };

        if (UsesResponsesEndpoint(model))
        {
            payload["input"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemMessage },
                new JsonObject { ["role"] = "user", ["content"] = prompt }
            };
        }
        else
        {
            payload["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemMessage },
                new JsonObject { ["role"] = "user", ["content"] = prompt }
            };
        }

        if (SupportsCustomTemperature(model))
        {
            payload["temperature"] = temperature;
        }

        if (RequiresMaxOutputTokens(model))
        {
            payload["max_output_tokens"] = maxTokens;
        }
        else
        {
            payload["max_tokens"] = maxTokens;
        }

        return payload;
    }

    public static bool UsesResponsesEndpoint(string model)
    {
        return IsGpt5Model(model);
    }

    private static bool SupportsCustomTemperature(string model)
    {
        return !IsGpt5Model(model);
    }

    private static bool RequiresMaxOutputTokens(string model)
    {
        return IsGpt5Model(model);
    }

    private static bool IsGpt5Model(string model)
    {
        return model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
    }
}

public static class AiStorySystemPrompts
{
    public const string Default = "You are a creative, historically knowledgeable city tour guide. Craft short stories and responses about buildings that feel authentic, welcoming, and vivid.";
}
