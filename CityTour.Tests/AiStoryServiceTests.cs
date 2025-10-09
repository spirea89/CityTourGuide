using System.Text.Json;
using System.Text.Json.Nodes;
using CityTour.Core.Services;
using Xunit;

namespace CityTour.Tests;

public class AiStoryServiceTests
{
    [Fact]
    public void CreateChatCompletionPayload_UsesMaxTokensForGpt5()
    {
        var payload = AiStoryPayloadFactory.Create("gpt-5", "prompt", 0.5, 42);

        Assert.True(payload.TryGetPropertyValue("max_completion_tokens", out var maxTokens));
        Assert.Equal(42, maxTokens!.GetValue<int>());
        Assert.False(payload.ContainsKey("max_tokens"));
    }

    [Fact]
    public void TryExtractCompletionContent_ReadsResponseOutput()
    {
        const string json = """
        {
            "response": {
                "status": "completed",
                "output": [
                    {
                        "type": "message",
                        "role": "assistant",
                        "content": [
                            {
                                "type": "output_text",
                                "text": "This is the story."
                            }
                        ]
                    }
                ]
            }
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("This is the story.", content);
    }

    [Fact]
    public void TryExtractCompletionContent_ReadsNestedOutputTextContent()
    {
        const string json = """
        {
            "response": {
                "status": "completed",
                "output": [
                    {
                        "type": "message",
                        "role": "assistant",
                        "content": [
                            {
                                "type": "output_text",
                                "content": [
                                    {
                                        "type": "text",
                                        "text": "Nested story paragraph."
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("Nested story paragraph.", content);
    }

    [Fact]
    public void TryExtractCompletionContent_ReadsOutputTextArray()
    {
        const string json = """
        {
            "output_text": [
                "First paragraph.",
                "Second paragraph."
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("First paragraph.\n\nSecond paragraph.", content);
    }

    [Fact]
    public void TryExtractCompletionContent_ReadsResultEnvelope()
    {
        const string json = """
        {
            "result": {
                "output": [
                    {
                        "type": "message",
                        "role": "assistant",
                        "content": [
                            {
                                "type": "output_text",
                                "text": "Result envelope story."
                            }
                        ]
                    }
                ]
            }
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("Result envelope story.", content);
    }

    [Fact]
    public void TryExtractCompletionContent_ReadsDataArrayFallback()
    {
        const string json = """
        {
            "data": [
                {
                    "message": {
                        "role": "assistant",
                        "content": [
                            {
                                "type": "text",
                                "text": "Fallback story."
                            }
                        ]
                    }
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("Fallback story.", content);
    }

    [Fact]
    public void TryExtractCompletionContent_ReadsItemsCollection()
    {
        const string json = """
        {
            "response": {
                "status": "completed",
                "items": [
                    {
                        "type": "message",
                        "role": "assistant",
                        "items": [
                            {
                                "type": "output_text",
                                "text": "Items based story."
                            }
                        ]
                    }
                ]
            }
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("Items based story.", content);
    }

    [Fact]
    public void TryExtractCompletionContent_CombinesContentAndItems()
    {
        const string json = """
        {
            "response": {
                "status": "completed",
                "output": [
                    {
                        "type": "message",
                        "role": "assistant",
                        "content": [
                            {
                                "type": "output_text",
                                "text": "First paragraph."
                            }
                        ],
                        "items": [
                            {
                                "type": "output_text",
                                "items": [
                                    {
                                        "type": "text",
                                        "text": "Second paragraph."
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("First paragraph.\n\nSecond paragraph.", content);
    }
}
