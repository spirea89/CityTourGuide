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
}
