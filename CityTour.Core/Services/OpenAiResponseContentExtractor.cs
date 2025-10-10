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
        },
        StringComparer.Ordinal);

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
        var paragraphs = new List<string>();
        var reasoningFallback = new List<string>();
        CollectText(root, paragraphs, reasoningFallback, null);

        if (paragraphs.Count > 0)
        {
            return string.Join("\n\n", paragraphs);
        }

        return reasoningFallback.Count == 0 ? null : string.Join("\n\n", reasoningFallback);
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
