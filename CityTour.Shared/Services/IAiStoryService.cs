using CityTour.Models;

namespace CityTour.Services;

public interface IAiStoryService
{
    string CurrentModel { get; }

    Task<StoryGenerationResult> GenerateStoryAsync(
        string buildingName,
        string? buildingAddress,
        StoryCategory category,
        string? facts = null,
        string? language = null,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default);

    Task<StoryFactCheckResult> GenerateStoryWithFactCheckAsync(
        string buildingName,
        string? buildingAddress,
        StoryCategory category,
        string? facts = null,
        string? language = null,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default);

    Task<string> AskAddressDetailsAsync(
        string buildingName,
        string? buildingAddress,
        string? currentStory,
        string question,
        CancellationToken cancellationToken = default);

    string BuildStoryPrompt(
        string buildingName,
        string? buildingAddress,
        StoryCategory category,
        string? facts = null,
        string? language = null);

    void SetModel(string model);
}
