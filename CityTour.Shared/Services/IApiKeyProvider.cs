namespace CityTour.Services;

public interface IApiKeyProvider
{
    string? OpenAiApiKey { get; }
    string? GoogleMapsApiKey { get; }
    string? GooglePlacesApiKey { get; }
    string? WikipediaApiKey { get; }
}
