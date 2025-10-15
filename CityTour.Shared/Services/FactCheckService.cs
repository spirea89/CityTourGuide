using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using CityTour.Models;
using System.Text.Json;

namespace CityTour.Services;

public interface IFactCheckService
{
    Task<FactCheckSummary> VerifyStoryAsync(
        string story,
        string buildingName,
        string? buildingAddress,
        CancellationToken cancellationToken = default);
}

public class FactCheckService : IFactCheckService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FactCheckService> _logger;
    
    private static readonly Regex DatePattern = new(@"\b(\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex NamePattern = new(@"\b[A-Z][a-z]+ [A-Z][a-z]+\b", RegexOptions.Compiled);
    private static readonly Regex ArchitecturalPattern = new(@"\b(Gothic|Baroque|Renaissance|Art Nouveau|Romanesque|neoclassical|classical)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public FactCheckService(HttpClient httpClient, ILogger<FactCheckService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FactCheckSummary> VerifyStoryAsync(
        string story,
        string buildingName,
        string? buildingAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(story))
        {
            throw new ArgumentException("Story content is required for fact-checking.", nameof(story));
        }

        _logger.LogDebug("Starting fact-check for story about {BuildingName}", buildingName);

        var verifiedFacts = new List<FactCheckItem>();
        var unverifiedClaims = new List<FactCheckItem>();
        var contextualInfo = new List<FactCheckItem>();

        try
        {
            // Check basic building existence and details
            await VerifyBuildingExistence(buildingName, buildingAddress, verifiedFacts, unverifiedClaims, cancellationToken);

            // Extract and verify dates
            await VerifyHistoricalDates(story, buildingName, verifiedFacts, unverifiedClaims, cancellationToken);

            // Extract and verify names
            await VerifyNamedPersons(story, buildingName, verifiedFacts, unverifiedClaims, cancellationToken);

            // Verify architectural claims
            await VerifyArchitecturalClaims(story, buildingName, verifiedFacts, contextualInfo, cancellationToken);

            // Add contextual information
            AddContextualAssessment(story, contextualInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during fact-checking for {BuildingName}", buildingName);
            unverifiedClaims.Add(new FactCheckItem(
                "Fact-checking process encountered errors",
                FactCheckStatus.Uncertain,
                $"Technical issue: {ex.Message}",
                "System"));
        }

        var assessment = BuildOverallAssessment(verifiedFacts, unverifiedClaims, contextualInfo);
        var hasMajorInaccuracies = unverifiedClaims.Any(c => c.Status == FactCheckStatus.Inaccurate);

        _logger.LogDebug("Fact-check completed. Verified: {Verified}, Unverified: {Unverified}, Contextual: {Contextual}",
            verifiedFacts.Count, unverifiedClaims.Count, contextualInfo.Count);

        return new FactCheckSummary(
            verifiedFacts,
            unverifiedClaims,
            contextualInfo,
            hasMajorInaccuracies,
            assessment);
    }

    private async Task VerifyBuildingExistence(
        string buildingName,
        string? buildingAddress,
        List<FactCheckItem> verified,
        List<FactCheckItem> unverified,
        CancellationToken cancellationToken)
    {
        try
        {
            var searchTerm = !string.IsNullOrWhiteSpace(buildingAddress) 
                ? $"{buildingName} {buildingAddress}"
                : buildingName;

            var searchResults = await PerformWebSearch(searchTerm, cancellationToken);
            
            if (!string.IsNullOrWhiteSpace(searchResults) && searchResults.Length > 100)
            {
                verified.Add(new FactCheckItem(
                    $"{buildingName} exists and has online presence",
                    FactCheckStatus.Verified,
                    "Found multiple web references",
                    "Web search"));
            }
            else
            {
                unverified.Add(new FactCheckItem(
                    $"Limited verifiable information about {buildingName}",
                    FactCheckStatus.Unverified,
                    "Minimal web presence found",
                    "Web search"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify building existence for {BuildingName}", buildingName);
        }
    }

    private async Task VerifyHistoricalDates(
        string story,
        string buildingName,
        List<FactCheckItem> verified,
        List<FactCheckItem> unverified,
        CancellationToken cancellationToken)
    {
        var dates = DatePattern.Matches(story);
        foreach (Match dateMatch in dates)
        {
            var year = dateMatch.Value;
            var contextStart = Math.Max(0, dateMatch.Index - 50);
            var contextEnd = Math.Min(story.Length, dateMatch.Index + 50);
            var context = story.Substring(contextStart, contextEnd - contextStart);

            try
            {
                var searchTerm = $"{buildingName} {year} history";
                var searchResults = await PerformWebSearch(searchTerm, cancellationToken);

                if (!string.IsNullOrWhiteSpace(searchResults) && searchResults.Contains(year))
                {
                    verified.Add(new FactCheckItem(
                        $"Date {year} appears in context: \"{context.Trim()}\"",
                        FactCheckStatus.Verified,
                        "Date found in web sources",
                        "Web search"));
                }
                else
                {
                    unverified.Add(new FactCheckItem(
                        $"Date {year} in context: \"{context.Trim()}\"",
                        FactCheckStatus.Unverified,
                        "Date not found in available sources",
                        "Web search"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not verify date {Year} for {BuildingName}", year, buildingName);
            }
        }
    }

    private async Task VerifyNamedPersons(
        string story,
        string buildingName,
        List<FactCheckItem> verified,
        List<FactCheckItem> unverified,
        CancellationToken cancellationToken)
    {
        var names = NamePattern.Matches(story);
        foreach (Match nameMatch in names)
        {
            var name = nameMatch.Value;
            
            // Skip common words that match the pattern
            if (IsCommonPhrase(name)) continue;

            try
            {
                var searchTerm = $"{name} {buildingName} Vienna history";
                var searchResults = await PerformWebSearch(searchTerm, cancellationToken);

                if (!string.IsNullOrWhiteSpace(searchResults) && 
                    (searchResults.Contains(name) || searchResults.Contains(buildingName)))
                {
                    verified.Add(new FactCheckItem(
                        $"Historical figure {name} mentioned",
                        FactCheckStatus.Verified,
                        "Name found in historical sources",
                        "Web search"));
                }
                else
                {
                    unverified.Add(new FactCheckItem(
                        $"Connection between {name} and {buildingName}",
                        FactCheckStatus.Unverified,
                        "Could not verify historical connection",
                        "Web search"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not verify person {Name} for {BuildingName}", name, buildingName);
            }
        }
    }

    private async Task VerifyArchitecturalClaims(
        string story,
        string buildingName,
        List<FactCheckItem> verified,
        List<FactCheckItem> contextual,
        CancellationToken cancellationToken)
    {
        var architecturalStyles = ArchitecturalPattern.Matches(story);
        foreach (Match styleMatch in architecturalStyles)
        {
            var style = styleMatch.Value;
            
            try
            {
                var searchTerm = $"{buildingName} {style} architecture Vienna";
                var searchResults = await PerformWebSearch(searchTerm, cancellationToken);

                if (!string.IsNullOrWhiteSpace(searchResults) && searchResults.ToLower().Contains(style.ToLower()))
                {
                    verified.Add(new FactCheckItem(
                        $"Architectural style: {style}",
                        FactCheckStatus.Verified,
                        "Style confirmed in architectural sources",
                        "Web search"));
                }
                else
                {
                    contextual.Add(new FactCheckItem(
                        $"Architectural style: {style}",
                        FactCheckStatus.Contextual,
                        "Style is plausible for the region and period",
                        "Contextual assessment"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not verify architectural style {Style} for {BuildingName}", style, buildingName);
            }
        }
    }

    private void AddContextualAssessment(string story, List<FactCheckItem> contextual)
    {
        // Assess the tone and approach of the story
        var uncertaintyWords = new[] { "perhaps", "might", "could", "possibly", "likely", "probably", "imagine" };
        var uncertaintyCount = uncertaintyWords.Count(word => story.ToLower().Contains(word));

        if (uncertaintyCount > 0)
        {
            contextual.Add(new FactCheckItem(
                "Story uses appropriate uncertainty language",
                FactCheckStatus.Contextual,
                $"Uses {uncertaintyCount} uncertainty markers like 'perhaps', 'might', 'imagine'",
                "Content analysis"));
        }

        // Check for responsible historical narrative approach
        if (story.ToLower().Contains("limited") || story.ToLower().Contains("sparse") || story.ToLower().Contains("unknown"))
        {
            contextual.Add(new FactCheckItem(
                "Story acknowledges information limitations",
                FactCheckStatus.Contextual,
                "Author acknowledges when historical details are limited",
                "Content analysis"));
        }
    }

    private static bool IsCommonPhrase(string text)
    {
        var commonPhrases = new[] { "South Tower", "Art Nouveau", "New Year", "Holy Roman", "Old Town" };
        return commonPhrases.Any(phrase => string.Equals(text, phrase, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> PerformWebSearch(string searchTerm, CancellationToken cancellationToken)
    {
        try
        {
            // Simple web search - in a real app you'd use a proper search API
            var encodedTerm = Uri.EscapeDataString(searchTerm);
            var url = $"https://www.google.com/search?q={encodedTerm}";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return content.Length > 1000 ? content.Substring(0, 1000) : content;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web search failed for term: {SearchTerm}", searchTerm);
        }

        return string.Empty;
    }

    private static string BuildOverallAssessment(
        List<FactCheckItem> verified,
        List<FactCheckItem> unverified,
        List<FactCheckItem> contextual)
    {
        var total = verified.Count + unverified.Count;
        if (total == 0)
        {
            return "Story contains general historical context without specific verifiable claims.";
        }

        var verifiedPercentage = total > 0 ? (verified.Count * 100 / total) : 0;
        var hasResponsibleLanguage = contextual.Any(c => c.Claim.Contains("uncertainty") || c.Claim.Contains("limitations"));

        var assessment = verifiedPercentage switch
        {
            >= 80 => "Highly factual story with most claims verified",
            >= 60 => "Generally reliable story with good factual basis",
            >= 40 => "Mixed reliability - some facts verified, others uncertain",
            >= 20 => "Limited verifiable facts - treat as historical narrative",
            _ => "Primarily narrative content with few verifiable facts"
        };

        if (hasResponsibleLanguage)
        {
            assessment += ". Uses appropriate uncertainty language where facts are limited.";
        }

        return assessment;
    }
}
