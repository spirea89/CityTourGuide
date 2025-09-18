namespace CityTour.Services;

/// <summary>
/// Configuration options that describe the primary city the application
/// focuses on. These values can be overridden through configuration files
/// or environment variables but sensible defaults are provided so the
/// application continues to work when no configuration is supplied.
/// </summary>
public sealed class CitySettings
{
    /// <summary>
    /// Gets or sets the display name of the city.
    /// </summary>
    public string Name { get; set; } = "Vienna";

    /// <summary>
    /// Gets or sets the latitude of the map's initial focus.
    /// </summary>
    public double Latitude { get; set; } = 48.2082;

    /// <summary>
    /// Gets or sets the longitude of the map's initial focus.
    /// </summary>
    public double Longitude { get; set; } = 16.3738;

    /// <summary>
    /// Gets or sets the radius (in kilometres) used when centring the map.
    /// </summary>
    public double DefaultMapRadiusKilometres { get; set; } = 3;

    /// <summary>
    /// Gets or sets the Google Places API key used for autocomplete and
    /// place lookups.
    /// </summary>
    public string GooglePlacesApiKey { get; set; } = "AIzaSyD1K-t8tsPgwbQUD888Xh9kQDT5w6sWIfc";

    /// <summary>
    /// Gets or sets the radius (in metres) used when looking up nearby
    /// places for a tapped map location.
    /// </summary>
    public double NearbySearchRadiusMetres { get; set; } = 50.0;

    /// <summary>
    /// Gets or sets the ISO region code supplied to the Google Places API.
    /// </summary>
    public string GoogleRegionCode { get; set; } = "AT";

    /// <summary>
    /// Gets or sets the language code supplied to the Google Places API.
    /// </summary>
    public string GoogleLanguageCode { get; set; } = "en";

    /// <summary>
    /// Creates a new <see cref="CitySettings"/> instance populated with the
    /// default values used by the app when configuration is not supplied.
    /// </summary>
    public static CitySettings CreateDefault() => new();
}
