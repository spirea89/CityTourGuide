using Microsoft.Extensions.Logging;

namespace CityTour.Services;

public class WebApiKeyProvider : IApiKeyProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebApiKeyProvider> _logger;
    private string? _cachedApiKeys;

    public WebApiKeyProvider(HttpClient httpClient, ILogger<WebApiKeyProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string? OpenAiApiKey => GetApiKey("openai");

    public string? GoogleMapsApiKey => GetApiKey("google_maps");

    public string? GooglePlacesApiKey => GetApiKey("google_places");

    public string? WikipediaApiKey => GetApiKey("wikipedia");

    private string? GetApiKey(string keyName)
    {
        try
        {
            if (_cachedApiKeys == null)
            {
                LoadApiKeys();
            }

            if (_cachedApiKeys != null)
            {
                // Parse JSON to get the specific key
                var json = System.Text.Json.JsonDocument.Parse(_cachedApiKeys);
                if (json.RootElement.TryGetProperty(keyName, out var keyElement))
                {
                    return keyElement.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load API key: {KeyName}", keyName);
        }

        return null;
    }

    private void LoadApiKeys()
    {
        try
        {
            // For web, we'll need to load this from the server or environment
            // For now, we'll use environment variables as fallback
            var apiKeysJson = Environment.GetEnvironmentVariable("API_KEYS_JSON");
            if (!string.IsNullOrEmpty(apiKeysJson))
            {
                _cachedApiKeys = apiKeysJson;
                return;
            }

            // Try individual environment variables
            var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var googleMapsKey = Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY");
            var googlePlacesKey = Environment.GetEnvironmentVariable("GOOGLE_PLACES_API_KEY");
            var wikipediaKey = Environment.GetEnvironmentVariable("WIKIPEDIA_API_KEY");

            if (!string.IsNullOrEmpty(openaiKey) || !string.IsNullOrEmpty(googleMapsKey))
            {
                var apiKeys = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(openaiKey)) apiKeys["openai"] = openaiKey;
                if (!string.IsNullOrEmpty(googleMapsKey)) apiKeys["google_maps"] = googleMapsKey;
                if (!string.IsNullOrEmpty(googlePlacesKey)) apiKeys["google_places"] = googlePlacesKey;
                if (!string.IsNullOrEmpty(wikipediaKey)) apiKeys["wikipedia"] = wikipediaKey;

                _cachedApiKeys = System.Text.Json.JsonSerializer.Serialize(apiKeys);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load API keys from environment");
        }
    }
}
