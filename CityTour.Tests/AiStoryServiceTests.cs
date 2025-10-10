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
    public void TryExtractCompletionContent_IgnoresContentFilterMetadata()
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
                                "text": {
                                    "value": "Story from value node."
                                }
                            }
                        ]
                    }
                ],
                "content_filter_results": {
                    "hate": {
                        "filtered": false,
                        "severity": "safe",
                        "source": "default"
                    }
                }
            }
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("Story from value node.", content);
    }

    [Fact]
    public void TryExtractCompletionContent_IgnoresMessageMetadata()
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
                                "text": "Story without metadata noise."
                            }
                        ],
                        "metadata": {
                            "source": "default"
                        }
                    }
                ]
            }
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("Story without metadata noise.", content);
    }

    [Fact]
    public void TryExtractCompletionContent_IgnoresAnnotationsMetadata()
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
                                "text": "Historical tale.",
                                "annotations": [
                                    {
                                        "type": "citation",
                                        "text": "[1]",
                                        "metadata": {
                                            "source": "default"
                                        }
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

        Assert.Equal("Historical tale.", content);
    }

    [Fact]
    public void TryExtractCompletionContent_IgnoresSourceProperty()
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
                                "text": "Story text.",
                                "source": "default"
                            }
                        ]
                    }
                ]
            }
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("Story text.", content);
    }

    [Fact]
    public void TryExtractCompletionContent_IgnoresToolUseMetadata()
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
                                "type": "tool_use",
                                "name": "default",
                                "input": {
                                    "query": "When was the building opened?"
                                }
                            },
                            {
                                "type": "output_text",
                                "text": "Tool-free story content."
                            }
                        ]
                    }
                ]
            }
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("Tool-free story content.", content);
    }

    [Fact]
    public void TryExtractCompletionContent_ReadsChoicesMessage()
    {
        const string json = """
        {
            "choices": [
                {
                    "index": 0,
                    "message": {
                        "role": "assistant",
                        "content": [
                            {
                                "type": "text",
                                "text": "Choice message story."
                            }
                        ]
                    },
                    "finish_reason": "stop"
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("Choice message story.", content);
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

    [Fact]
    public void TryExtractCompletionContent_PrefersOutputTextOverReasoning()
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
                                "type": "reasoning",
                                "summary": {
                                    "type": "output_text",
                                    "text": "High-level reasoning summary."
                                }
                            },
                            {
                                "type": "output_text",
                                "text": [
                                    {
                                        "type": "paragraph",
                                        "text": "Actual story paragraph."
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

        Assert.Equal("Actual story paragraph.", content);
    }

    [Fact]
    public void TryExtractCompletionContent_FallsBackToReasoningSummary()
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
                                "type": "reasoning",
                                "summary": {
                                    "type": "output_text",
                                    "content": [
                                        {
                                            "type": "text",
                                            "text": "Fallback reasoning summary."
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                ]
            }
        }
        """;

        using var document = JsonDocument.Parse(json);

        var content = OpenAiResponseContentExtractor.TryExtractCompletionContent(document.RootElement);

        Assert.Equal("Fallback reasoning summary.", content);
    }
}
