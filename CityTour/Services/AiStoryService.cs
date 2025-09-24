using System;
using System.Globalization;
using System.Text;
using CityTour.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

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

public class AiStoryService : IAiStoryService
{
    private const string DefaultModel = "wikipedia-summary";
    private const string ModelPreferenceKey = "ai.story.model";

    private readonly ILogger<AiStoryService> _logger;
    private readonly IWikipediaService _wikipediaService;
    private string _model;

    public AiStoryService(ILogger<AiStoryService> logger, IWikipediaService wikipediaService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _wikipediaService = wikipediaService ?? throw new ArgumentNullException(nameof(wikipediaService));

        var savedModel = Preferences.Get(ModelPreferenceKey, DefaultModel);
        _model = string.IsNullOrWhiteSpace(savedModel) ? DefaultModel : savedModel.Trim();
    }

    public string CurrentModel => _model;

    public void SetModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("A model name is required.", nameof(model));
        }

        var trimmed = model.Trim();
        if (!string.Equals(trimmed, DefaultModel, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only the Wikipedia story source is currently supported.");
        }

        if (string.Equals(_model, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _model = DefaultModel;
        Preferences.Set(ModelPreferenceKey, _model);
    }

    public async Task<StoryGenerationResult> GenerateStoryAsync(
        string buildingName,
        string? buildingAddress,
        StoryCategory category,
        string? facts = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildingName) && string.IsNullOrWhiteSpace(buildingAddress))
        {
            throw new ArgumentException("A building name or address is required.", nameof(buildingName));
        }

        var resolvedFacts = ResolveFacts(facts);
        var resolvedName = ResolveBuildingName(buildingName, buildingAddress);
        var resolvedAddress = ResolveAddress(buildingAddress);
        var resolvedLanguage = ResolveLanguage(language);
        var prompt = BuildPromptFromResolved(resolvedName, resolvedAddress, category, resolvedFacts, resolvedLanguage);

        var summary = await FetchWikipediaSummaryAsync(buildingName, buildingAddress, cancellationToken);
        var story = BuildStoryFromSources(resolvedName, resolvedAddress, category, resolvedFacts, summary);
        var promptWithSummary = AppendSummaryToPrompt(prompt, summary);

        return new StoryGenerationResult(story, promptWithSummary);
    }

    public async Task<string> AskAddressDetailsAsync(
        string buildingName,
        string? buildingAddress,
        string? currentStory,
        string question,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildingName) && string.IsNullOrWhiteSpace(buildingAddress))
        {
            throw new ArgumentException("A building name or address is required.", nameof(buildingName));
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("A question is required.", nameof(question));
        }

        _ = currentStory;

        var resolvedName = ResolveBuildingName(buildingName, buildingAddress);
        var resolvedAddress = ResolveAddress(buildingAddress);
        var summary = await FetchWikipediaSummaryAsync(buildingName, buildingAddress, cancellationToken);

        if (summary is null)
        {
            var locationLabel = BuildLocationLabel(resolvedName, resolvedAddress);
            return $"Wikipedia does not currently provide additional details about {locationLabel}.";
        }

        var builder = new StringBuilder();
        var location = BuildLocationLabel(resolvedName, resolvedAddress);
        builder.AppendLine($"Wikipedia may not directly answer \"{question}\", but here is the available overview of {location}:");

        var extract = !string.IsNullOrWhiteSpace(summary.Extract)
            ? summary.Extract!.Trim()
            : summary.Description?.Trim();

        if (!string.IsNullOrWhiteSpace(extract))
        {
            builder.AppendLine();
            builder.AppendLine(extract);
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine("The article does not contain a readable summary yet.");
        }

        if (!string.IsNullOrWhiteSpace(summary.Url))
        {
            builder.AppendLine();
            builder.AppendLine($"Source: {summary.Url}");
        }

        return builder.ToString().Trim();
    }

    public string BuildStoryPrompt(
        string buildingName,
        string? buildingAddress,
        StoryCategory category,
        string? facts = null,
        string? language = null)
    {
        var resolvedFacts = ResolveFacts(facts);
        var resolvedName = ResolveBuildingName(buildingName, buildingAddress);
        var resolvedAddress = ResolveAddress(buildingAddress);
        var resolvedLanguage = ResolveLanguage(language);

        return BuildPromptFromResolved(resolvedName, resolvedAddress, category, resolvedFacts, resolvedLanguage);
    }

    private static string BuildPromptFromResolved(
        string resolvedName,
        string resolvedAddress,
        StoryCategory category,
        string resolvedFacts,
        string resolvedLanguage)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Story inputs");
        builder.AppendLine("-------------");
        builder.AppendLine("Source: Wikipedia summaries and verified facts.");
        builder.AppendLine($"Focus: {GetCategoryLabel(category)}");
        builder.AppendLine($"Building: {resolvedName}");

        if (!string.IsNullOrWhiteSpace(resolvedAddress) && !string.Equals(resolvedAddress, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine($"Address hint: {resolvedAddress}");
        }

        builder.AppendLine($"Preferred language: {resolvedLanguage}");
        builder.AppendLine();
        builder.AppendLine("Verified facts:");
        builder.AppendLine(resolvedFacts);

        return builder.ToString().Trim();
    }

    private async Task<WikipediaSummary?> FetchWikipediaSummaryAsync(
        string buildingName,
        string? buildingAddress,
        CancellationToken cancellationToken)
    {
        var queryName = string.IsNullOrWhiteSpace(buildingName)
            ? (buildingAddress ?? string.Empty)
            : buildingName.Trim();
        var queryAddress = string.IsNullOrWhiteSpace(buildingAddress) ? null : buildingAddress.Trim();

        try
        {
            return await _wikipediaService.FetchSummaryAsync(
                queryName,
                queryAddress,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Wikipedia summary for {Name} / {Address}", queryName, queryAddress);
            throw;
        }
    }

    private static string BuildStoryFromSources(
        string resolvedName,
        string resolvedAddress,
        StoryCategory category,
        string resolvedFacts,
        WikipediaSummary? summary)
    {
        var builder = new StringBuilder();
        var location = BuildLocationLabel(resolvedName, resolvedAddress);
        builder.AppendLine(GetCategoryIntroduction(category, location, summary));
        builder.AppendLine();

        var extract = !string.IsNullOrWhiteSpace(summary?.Extract)
            ? summary!.Extract!.Trim()
            : summary?.Description?.Trim();

        if (!string.IsNullOrWhiteSpace(extract))
        {
            builder.AppendLine(extract!);
        }
        else
        {
            builder.AppendLine("Wikipedia does not currently offer a summary for this place. Rely on the verified facts below when sharing details.");
        }

        builder.AppendLine();
        builder.AppendLine("Verified facts:");
        builder.AppendLine(resolvedFacts);

        if (summary is not null)
        {
            if (!string.IsNullOrWhiteSpace(summary.Url))
            {
                builder.AppendLine();
                builder.AppendLine($"Source: {summary.Url}");
            }

            if (!string.IsNullOrWhiteSpace(summary.LastModified))
            {
                builder.AppendLine($"Last updated on Wikipedia: {summary.LastModified}");
            }
        }

        return builder.ToString().Trim();
    }

    private static string AppendSummaryToPrompt(string prompt, WikipediaSummary? summary)
    {
        if (summary is null)
        {
            return prompt;
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            builder.AppendLine(prompt.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("Wikipedia article snapshot:");

        if (!string.IsNullOrWhiteSpace(summary.Title))
        {
            builder.AppendLine($"Title: {summary.Title}");
        }

        var extract = !string.IsNullOrWhiteSpace(summary.Extract)
            ? summary.Extract!.Trim()
            : summary.Description?.Trim();

        if (!string.IsNullOrWhiteSpace(extract))
        {
            builder.AppendLine(extract!);
        }
        else
        {
            builder.AppendLine("No readable summary was provided.");
        }

        if (!string.IsNullOrWhiteSpace(summary.Url))
        {
            builder.AppendLine($"Source: {summary.Url}");
        }

        if (!string.IsNullOrWhiteSpace(summary.LastModified))
        {
            builder.AppendLine($"Last modified: {summary.LastModified}");
        }

        return builder.ToString().Trim();
    }

    private static string GetCategoryLabel(StoryCategory category)
    {
        return category switch
        {
            StoryCategory.History => "History",
            StoryCategory.Personalities => "Personalities",
            StoryCategory.Architecture => "Architecture",
            StoryCategory.Today => "Today",
            StoryCategory.Kids => "Kids",
            _ => category.ToString()
        };
    }

    private static string GetCategoryIntroduction(StoryCategory category, string location, WikipediaSummary? summary)
    {
        var descriptor = BuildArticleDescriptor(summary);
        return category switch
        {
            StoryCategory.History => $"Historical snapshot of {location} based on {descriptor}.",
            StoryCategory.Personalities => $"People connected to {location} according to {descriptor}.",
            StoryCategory.Architecture => $"Architectural notes for {location} drawn from {descriptor}.",
            StoryCategory.Today => $"How {location} is used today according to {descriptor}.",
            StoryCategory.Kids => $"Friendly highlights about {location} inspired by {descriptor}.",
            _ => $"Overview of {location} based on {descriptor}."
        };
    }

    private static string BuildArticleDescriptor(WikipediaSummary? summary)
    {
        if (summary is null)
        {
            return "available Wikipedia sources";
        }

        if (string.IsNullOrWhiteSpace(summary.Language))
        {
            return "the Wikipedia article";
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(summary.Language);
            return $"the {culture.EnglishName} Wikipedia article";
        }
        catch (CultureNotFoundException)
        {
            return $"the {summary.Language.ToUpperInvariant()} Wikipedia article";
        }
    }

    private static string BuildLocationLabel(string resolvedName, string resolvedAddress)
    {
        var name = string.IsNullOrWhiteSpace(resolvedName) ? "this place" : resolvedName.Trim();
        var address = string.IsNullOrWhiteSpace(resolvedAddress) || string.Equals(resolvedAddress, "unknown", StringComparison.OrdinalIgnoreCase)
            ? null
            : resolvedAddress.Trim();

        if (string.IsNullOrWhiteSpace(address) || string.Equals(name, address, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        if (string.Equals(name, "unknown building", StringComparison.OrdinalIgnoreCase))
        {
            return address;
        }

        return $"{name} ({address})";
    }

    private static string ResolveFacts(string? facts)
    {
        if (string.IsNullOrWhiteSpace(facts))
        {
            return "No verified facts were provided. Explain to the visitor that trustworthy details for this site could not be confirmed.";
        }

        var lines = facts
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var builder = new StringBuilder();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!line.StartsWith("-"))
            {
                builder.Append("- ");
            }

            builder.Append(line);
            builder.AppendLine();
        }

        var formatted = builder.ToString().TrimEnd();
        return formatted.Length > 0 ? formatted : facts.Trim();
    }

    private static string ResolveBuildingName(string buildingName, string? buildingAddress)
    {
        if (!string.IsNullOrWhiteSpace(buildingName))
        {
            return buildingName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(buildingAddress))
        {
            return buildingAddress.Trim();
        }

        return "unknown building";
    }

    private static string ResolveAddress(string? buildingAddress)
    {
        return string.IsNullOrWhiteSpace(buildingAddress) ? "unknown" : buildingAddress.Trim();
    }

    private static string ResolveLanguage(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            return language.Trim();
        }

        var culture = CultureInfo.CurrentUICulture ?? CultureInfo.CurrentCulture;
        var text = culture.EnglishName;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = culture.DisplayName;
        }

        return string.IsNullOrWhiteSpace(text) ? "English" : text;
    }
}
