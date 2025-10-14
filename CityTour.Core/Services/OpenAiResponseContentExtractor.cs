using System.Collections.Generic;
using System.Text.Json;

namespace CityTour.Core.Services;

public static class OpenAiResponseContentExtractor
{
    private static readonly HashSet<string> MetadataPropertyNames = new(
        new[]
        {
            "type",
            "role",
            "id",
            "index",
            "created",
            "created_at",
            "status",
            "object",
            "finish_reason",
            "model",
            "usage",
            "system_fingerprint",
            "content_filter_results",
            "metadata",
            "annotations",
            "source",
            "max_output_tokens",
            "max_tokens",
            "temperature",
            "top_p",
            "frequency_penalty",
            "presence_penalty",
            "stop",
            "stream",
            "logprobs",
            "echo",
            "n",
            "best_of",
            "user",
            "suffix",
            "logit_bias",
            "developer",
            "medium",
            "default",
            "auto",
            "disabled",
            "enabled",
            "prompt_tokens",
            "completion_tokens",
            "total_tokens",
            "prompt_logprobs",
            "input_tokens",
            "output_tokens",
            "cached_tokens",
        },
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] PrioritizedPropertyNames =
    {
        "text",
        "output_text",
        "content",
        "items",
        "value",
        "output",
        "choices",
        "messages",
        "message",
        "data",
        "results",
        "result",
        "response",
    };

    public static string? TryExtractCompletionContent(JsonElement root)
    {
        // Try standard OpenAI chat completion response format first
        var standardContent = TryExtractStandardChatResponse(root);
        if (!string.IsNullOrWhiteSpace(standardContent))
        {
            return standardContent;
        }

        // Try GPT-5 responses endpoint format
        var responsesContent = TryExtractResponsesEndpointFormat(root);
        if (!string.IsNullOrWhiteSpace(responsesContent))
        {
            return responsesContent;
        }

        // Fallback to generic extraction
        var paragraphs = new List<string>();
        var reasoningFallback = new List<string>();
        CollectText(root, paragraphs, reasoningFallback, null);

        if (paragraphs.Count > 0)
        {
            return string.Join("\n\n", paragraphs);
        }

        return reasoningFallback.Count == 0 ? null : string.Join("\n\n", reasoningFallback);
    }

    private static string? TryExtractStandardChatResponse(JsonElement root)
    {
        // Standard format: { "choices": [{ "message": { "content": "text" } }] }
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                {
                    if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    {
                        var text = content.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text.Trim();
                        }
                    }
                }
                
                // Also try direct text property in choice
                if (choice.TryGetProperty("text", out var choiceText) && choiceText.ValueKind == JsonValueKind.String)
                {
                    var text = choiceText.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }
        }

        return null;
    }

    private static string? TryExtractResponsesEndpointFormat(JsonElement root)
    {
        // GPT-5 responses format - may have different structure
        if (root.TryGetProperty("output", out var output))
        {
            if (output.ValueKind == JsonValueKind.String)
            {
                var text = output.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
            else if (output.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in output.EnumerateArray())
                {
                    if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    {
                        var text = content.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text.Trim();
                        }
                    }
                }
            }
        }

        // Try other possible response formats
        if (root.TryGetProperty("text", out var directText) && directText.ValueKind == JsonValueKind.String)
        {
            var text = directText.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }

    private static void CollectText(
        JsonElement element,
        List<string> paragraphs,
        List<string> reasoningFallback,
        string? contextType)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (string.Equals(contextType, "reasoning", StringComparison.OrdinalIgnoreCase))
                {
                    AppendParagraph(reasoningFallback, element.GetString());
                }
                else
                {
                    AppendParagraph(paragraphs, element.GetString());
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectText(item, paragraphs, reasoningFallback, contextType);
                }

                break;
            case JsonValueKind.Object:
                var typeContext = contextType;
                if (element.TryGetProperty("type", out var typeElement)
                    && typeElement.ValueKind == JsonValueKind.String)
                {
                    var typeValue = typeElement.GetString();
                    if (!string.IsNullOrWhiteSpace(typeValue))
                    {
                        typeContext = typeValue;
                    }
                }

                if (string.Equals(typeContext, "reasoning", StringComparison.OrdinalIgnoreCase))
                {
                    if (element.TryGetProperty("summary", out var summary))
                    {
                        CollectText(summary, reasoningFallback, reasoningFallback, typeContext);
                    }

                    return;
                }

                if (IsToolContext(typeContext))
                {
                    return;
                }

                var processedProperties = new HashSet<string>(StringComparer.Ordinal);

                foreach (var propertyName in PrioritizedPropertyNames)
                {
                    if (ProcessProperty(element, propertyName, paragraphs, reasoningFallback, typeContext))
                    {
                        processedProperties.Add(propertyName);
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (MetadataPropertyNames.Contains(property.Name))
                    {
                        continue;
                    }

                    if (processedProperties.Contains(property.Name))
                    {
                        continue;
                    }

                    CollectText(property.Value, paragraphs, reasoningFallback, typeContext);
                }

                break;
        }
    }

    private static bool IsToolContext(string? typeContext)
    {
        return string.Equals(typeContext, "tool_use", StringComparison.OrdinalIgnoreCase)
            || string.Equals(typeContext, "tool_result", StringComparison.OrdinalIgnoreCase)
            || string.Equals(typeContext, "tool_call", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ProcessProperty(
        JsonElement element,
        string name,
        List<string> paragraphs,
        List<string> reasoningFallback,
        string? contextType)
    {
        if (element.TryGetProperty(name, out var value))
        {
            var beforeCount = paragraphs.Count;
            var beforeFallback = reasoningFallback.Count;
            CollectText(value, paragraphs, reasoningFallback, contextType);
            return paragraphs.Count > beforeCount || reasoningFallback.Count > beforeFallback;
        }

        return false;
    }

    private static void AppendParagraph(List<string> paragraphs, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        paragraphs.Add(text.Trim());
    }
}
