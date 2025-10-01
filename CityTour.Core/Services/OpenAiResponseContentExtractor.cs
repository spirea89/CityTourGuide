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

                if (element.TryGetProperty("output", out var output))
                {
                    AppendTextFromElement(output, builder);
                }

                if (element.TryGetProperty("output_text", out var outputText))
                {
                    AppendTextFromElement(outputText, builder);
                }

                if (element.TryGetProperty("choices", out var nestedChoices))
                {
                    AppendTextFromElement(nestedChoices, builder);
                }

                if (element.TryGetProperty("messages", out var messages))
                {
                    AppendTextFromElement(messages, builder);
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
}
