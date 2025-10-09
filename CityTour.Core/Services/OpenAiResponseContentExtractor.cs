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
        },
        StringComparer.Ordinal);

    private static readonly HashSet<string> PrioritizedPropertyNames = new(
        new[]
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
        },
        StringComparer.Ordinal);

    public static string? TryExtractCompletionContent(JsonElement root)
    {
        var paragraphs = new List<string>();
        CollectText(root, paragraphs);

        return paragraphs.Count == 0 ? null : string.Join("\n\n", paragraphs);
    }

    private static void CollectText(JsonElement element, List<string> paragraphs)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AppendParagraph(paragraphs, element.GetString());
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectText(item, paragraphs);
                }

                break;
            case JsonValueKind.Object:
                if (TryHandleReasoningElement(element, paragraphs))
                {
                    return;
                }

                ProcessProperty(element, "text", paragraphs);
                ProcessProperty(element, "output_text", paragraphs);
                ProcessProperty(element, "content", paragraphs);
                ProcessProperty(element, "items", paragraphs);
                ProcessProperty(element, "value", paragraphs);
                ProcessProperty(element, "output", paragraphs);
                ProcessProperty(element, "choices", paragraphs);
                ProcessProperty(element, "messages", paragraphs);
                ProcessProperty(element, "message", paragraphs);
                ProcessProperty(element, "data", paragraphs);
                ProcessProperty(element, "results", paragraphs);
                ProcessProperty(element, "result", paragraphs);
                ProcessProperty(element, "response", paragraphs);

                foreach (var property in element.EnumerateObject())
                {
                    if (MetadataPropertyNames.Contains(property.Name))
                    {
                        continue;
                    }

                    if (PrioritizedPropertyNames.Contains(property.Name))
                    {
                        continue;
                    }

                    CollectText(property.Value, paragraphs);
                }

                break;
        }
    }

    private static bool TryHandleReasoningElement(JsonElement element, List<string> paragraphs)
    {
        if (!element.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var type = typeElement.GetString();
        if (!string.Equals(type, "reasoning", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (element.TryGetProperty("summary", out var summary))
        {
            CollectText(summary, paragraphs);
        }

        return true;
    }

    private static void ProcessProperty(JsonElement element, string name, List<string> paragraphs)
    {
        if (element.TryGetProperty(name, out var value))
        {
            CollectText(value, paragraphs);
        }
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
