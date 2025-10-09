using System.Text;
using System.Text.Json;

namespace CityTour.Core.Services;

public static class OpenAiResponseContentExtractor
{
    public static string? TryExtractCompletionContent(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ExtractTextFromElement(root);
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

        if (root.TryGetProperty("output", out var output))
        {
            var text = ExtractTextFromElement(output);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        if (root.TryGetProperty("output_text", out var outputText))
        {
            var text = ExtractTextFromElement(outputText);
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

        if (root.TryGetProperty("response", out var responseElement))
        {
            var text = TryExtractCompletionContent(responseElement);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        if (root.TryGetProperty("result", out var resultElement))
        {
            var text = TryExtractCompletionContent(resultElement);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        if (root.TryGetProperty("results", out var resultsElement))
        {
            var text = ExtractTextFromElement(resultsElement);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        if (root.TryGetProperty("data", out var dataElement))
        {
            var text = ExtractTextFromElement(dataElement);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        var fallback = ExtractTextFromElement(root);
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
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
            return textElement.ValueKind == JsonValueKind.String
                ? textElement.GetString()
                : ExtractTextFromElement(textElement);
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

    internal static string? ExtractTextFromElement(JsonElement element)
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
            {

                if (element.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
                {
                    var type = typeElement.GetString();
                    if (string.Equals(type, "reasoning", StringComparison.OrdinalIgnoreCase))
                    {
                        if (element.TryGetProperty("summary", out var summary))
                        {
                            AppendTextFromElement(summary, builder);
                        }

                        return;
                    }
                }

                AppendProperty("text");
                AppendProperty("content");
                AppendProperty("value");
                AppendProperty("output");
                AppendProperty("output_text");
                AppendProperty("choices");
                AppendProperty("messages");
                AppendProperty("items");

                foreach (var property in element.EnumerateObject())
                {
                    if (ShouldSkip(property))
                    {
                        continue;
                    }

                    var before = builder.Length;
                    AppendTextFromElement(property.Value, builder);

                    if (builder.Length > before)
                    {
                        // Keep gathering text from other properties in case the
                        // response spreads the content across multiple fields.
                        continue;
                    }
                }

                return;

                void AppendProperty(string name)
                {
                    if (element.TryGetProperty(name, out var value))
                    {
                        AppendTextFromElement(value, builder);
                    }
                }

                static bool ShouldSkip(JsonProperty property)
                {
                    if (property.NameEquals("type")
                        || property.NameEquals("role")
                        || property.NameEquals("id")
                        || property.NameEquals("index")
                        || property.NameEquals("created")
                        || property.NameEquals("created_at")
                        || property.NameEquals("status")
                        || property.NameEquals("object")
                        || property.NameEquals("finish_reason")
                        || property.NameEquals("model")
                        || property.NameEquals("usage")
                        || property.NameEquals("system_fingerprint")
                        || property.NameEquals("text")
                        || property.NameEquals("content")
                        || property.NameEquals("value")
                        || property.NameEquals("output")
                        || property.NameEquals("output_text")
                        || property.NameEquals("choices")
                        || property.NameEquals("messages")
                        || property.NameEquals("items"))
                    {
                        return true;
                    }

                    return false;
                }
            }
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
}
