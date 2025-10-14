using Microsoft.Extensions.Logging;

namespace CityTour.Services;

public interface IBuildingContextService
{
    Task<BuildingContextResult> GetEnhancedContextAsync(
        string buildingName,
        string? buildingAddress,
        string? existingFacts,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default);
}

public sealed record BuildingContextResult(
    string EnhancedFacts,
    string? WikipediaTitle,
    string? WikipediaUrl,
    bool HasWikipediaInfo);

public class BuildingContextService : IBuildingContextService
{
    private readonly IWikipediaService _wikipediaService;
    private readonly ILogger<BuildingContextService> _logger;

    public BuildingContextService(IWikipediaService wikipediaService, ILogger<BuildingContextService> logger)
    {
        _wikipediaService = wikipediaService ?? throw new ArgumentNullException(nameof(wikipediaService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BuildingContextResult> GetEnhancedContextAsync(
        string buildingName,
        string? buildingAddress,
        string? existingFacts,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildingName) && string.IsNullOrWhiteSpace(buildingAddress))
        {
            throw new ArgumentException("A building name or address is required.", nameof(buildingName));
        }

        var contextParts = new List<string>();
        
        // Add existing facts if available
        if (!string.IsNullOrWhiteSpace(existingFacts))
        {
            contextParts.Add($"Basic Info: {existingFacts.Trim()}");
        }

        // Try to get Wikipedia information
        WikipediaSummary? wikipediaInfo = null;
        try
        {
            _logger.LogDebug("Attempting to fetch Wikipedia information for {BuildingName} at {Address}", 
                buildingName, buildingAddress);
                
            wikipediaInfo = await _wikipediaService.FetchSummaryAsync(
                buildingName, 
                buildingAddress, 
                latitude, 
                longitude, 
                cancellationToken);

            if (wikipediaInfo != null)
            {
                _logger.LogDebug("Successfully retrieved Wikipedia information for {BuildingName}", buildingName);
                
                // Add Wikipedia extract if available
                if (!string.IsNullOrWhiteSpace(wikipediaInfo.Extract))
                {
                    contextParts.Add($"Historical Context: {wikipediaInfo.Extract.Trim()}");
                }
                
                // Add Wikipedia description if different from extract
                if (!string.IsNullOrWhiteSpace(wikipediaInfo.Description) && 
                    !string.Equals(wikipediaInfo.Description.Trim(), wikipediaInfo.Extract?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    contextParts.Add($"Description: {wikipediaInfo.Description.Trim()}");
                }
            }
            else
            {
                _logger.LogDebug("No Wikipedia information found for {BuildingName} at {Address}", 
                    buildingName, buildingAddress);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Wikipedia information for {BuildingName} at {Address}: {Error}", 
                buildingName, buildingAddress, ex.Message);
        }

        // Build enhanced facts string
        string enhancedFacts;
        if (contextParts.Count == 0)
        {
            // Provide basic location context when no facts are available
            var locationContext = BuildLocationContext(buildingName, buildingAddress);
            enhancedFacts = locationContext;
        }
        else
        {
            enhancedFacts = string.Join("\n\n", contextParts);
        }

        return new BuildingContextResult(
            enhancedFacts,
            wikipediaInfo?.Title,
            wikipediaInfo?.Url,
            wikipediaInfo != null);
    }

    private static string BuildLocationContext(string buildingName, string? buildingAddress)
    {
        var context = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(buildingName))
        {
            context.Add($"Building Name: {buildingName.Trim()}");
        }
        
        if (!string.IsNullOrWhiteSpace(buildingAddress))
        {
            context.Add($"Location: {buildingAddress.Trim()}");
            
            // Extract city information for additional context
            var parsed = AddressFormatter.Parse(buildingAddress);
            if (!string.IsNullOrWhiteSpace(parsed.City))
            {
                context.Add($"This building is located in {parsed.City}");
            }
        }

        return context.Count > 0 
            ? string.Join(". ", context) 
            : "Building information is limited";
    }
}
