using System.Text.Json.Nodes;
using CityTour.Services;
using Xunit;

namespace CityTour.Tests;

public class AiStoryServiceTests
{
    [Fact]
    public void CreateChatCompletionPayload_UsesMaxTokensForGpt5()
    {
        var payload = AiStoryService.CreateChatCompletionPayload("gpt-5", "prompt", 0.5, 42);

        Assert.True(payload.TryGetPropertyValue("max_tokens", out var maxTokens));
        Assert.Equal(42, maxTokens!.GetValue<int>());
        Assert.False(payload.ContainsKey("max_completion_tokens"));
    }
}
