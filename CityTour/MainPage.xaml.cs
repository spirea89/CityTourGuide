using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using CityTour.Services;
using CityTour.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage; // Preferences

namespace CityTour
{
    public partial class MainPage : ContentPage
    {
    private readonly PlaceService _service;
    private readonly IAiStoryService _storyService;
    private readonly IApiKeyProvider _apiKeys;
    private readonly IWikipediaService _wikipediaService;
    private List<Place> _allPlaces = new List<Place>();

    // Google Places (Web)
    private readonly HttpClient _http = new HttpClient();

    // Live suggestions
    private CancellationTokenSource? _typeCts;

    // Building selection mode
    private bool _isSelectingBuilding = false;
    private bool _isStreetViewVisible;
    private Location? _streetViewLocation;
    private const string SavedBuildingsKey = "citytour.saved_buildings";

    public MainPage(PlaceService service, IAiStoryService storyService, IApiKeyProvider apiKeyProvider, IWikipediaService wikipediaService)
    {
        InitializeComponent();
        _service = service;
        _storyService = storyService;
        _apiKeys = apiKeyProvider;
        _wikipediaService = wikipediaService;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Load local places
        _allPlaces = _service.GetAll().ToList();

        // Center on Vienna
        var vienna = new Location(48.2082, 16.3738);
        Map.MoveToRegion(MapSpan.FromCenterAndRadius(vienna, Distance.FromKilometers(3)));
        SetStreetViewLocation(vienna);

        // Draw pins
        RefreshPins(_allPlaces);

        // Map click handler (for building selection)
        Map.MapClicked += OnMapClicked;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Map.MapClicked -= OnMapClicked;
    }

    private void RefreshPins(IEnumerable<Place> places)
    {
        Map.Pins.Clear();

        foreach (var p in places)
        {
            var pin = CreateStoryPin(p.Id, p.Name, p.Address, new Location(p.Latitude, p.Longitude));
            Map.Pins.Add(pin);
        }

        // Also show saved buildings
        foreach (var b in LoadSavedBuildings())
        {
            var pin = CreateStoryPin(b.PlaceId, b.Name, b.Address, new Location(b.Latitude, b.Longitude), isSaved: true);
            Map.Pins.Add(pin);
        }
    }

    private void ApplyFilter(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            RefreshPins(_allPlaces);
            return;
        }

        var filter = text.Trim();
        if (filter.Length == 0)
        {
            RefreshPins(_allPlaces);
            return;
        }

        var filtered = _allPlaces
            .Where(p => AddressMatches(p, filter))
            .ToList();

        RefreshPins(filtered);
    }

    private static bool AddressMatches(Place place, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var rawAddress = place.Address ?? string.Empty;
        if (rawAddress.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parsed = AddressFormatter.Parse(rawAddress);

        return MatchesAddressSegment(parsed.StreetAndNumber, filter)
            || MatchesAddressSegment(parsed.City, filter)
            || MatchesAddressSegment(parsed.Country, filter)
            || MatchesAddressSegment(parsed.DisplayAddress, filter)
            || MatchesAddressSegment(parsed.StoryAddress, filter)
            || MatchesAddressSegment(parsed.Original, filter);
    }

    private static bool MatchesAddressSegment(string? segment, string filter)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var compareInfo = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreSymbols;
        return compareInfo.IndexOf(segment, filter, options) >= 0;
    }

    // SearchBar events
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter(e.NewTextValue);

        // Debounced Google autocomplete
        _typeCts?.Cancel();
        _typeCts = new CancellationTokenSource();
        var token = _typeCts.Token;
        _ = DebouncedSuggestionsAsync(e.NewTextValue, token);
    }

    private async Task DebouncedSuggestionsAsync(string? input, CancellationToken ct)
    {
        try { await Task.Delay(250, ct); }
        catch (TaskCanceledException) { return; }

        if (string.IsNullOrWhiteSpace(input))
        {
            ResultsList.IsVisible = false;
            ResultsList.ItemsSource = null;
            return;
        }

        List<SuggestionItem> items;
        try
        {
            items = await GetAutocompleteAsync(input);
        }
        catch (InvalidOperationException ex)
        {
            await DisplayAlert("Configuration error", ex.Message, "OK");
            ResultsList.IsVisible = false;
            ResultsList.ItemsSource = null;
            return;
        }
        if (items.Count == 0)
        {
            ResultsList.IsVisible = false;
            ResultsList.ItemsSource = null;
            return;
        }

        ResultsList.ItemsSource = items;
        ResultsList.IsVisible = true;
    }

    private async void OnSearchButtonPressed(object sender, EventArgs e)
    {
        var query = Search.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            ApplyFilter(query);
            ResultsList.IsVisible = false;
            return;
        }

        ApplyFilter(query);
        ResultsList.IsVisible = false;

        await TryGoogleSearchAsync(query);
    }

    private async void OnSuggestionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.FirstOrDefault() is not SuggestionItem item)
            return;

        ResultsList.SelectedItem = null;
        ResultsList.IsVisible = false;
        Search.Text = item.PrimaryText;

        await TryPlaceIdAsync(item.PlaceId);
    }

    private void OnRecenterClicked(object sender, EventArgs e)
    {
        var vienna = new Location(48.2082, 16.3738);
        Map.MoveToRegion(MapSpan.FromCenterAndRadius(vienna, Distance.FromKilometers(3)));
        SetStreetViewLocation(vienna);
    }

    private void OnStreetViewToggleClicked(object? sender, EventArgs e)
    {
        _isStreetViewVisible = !_isStreetViewVisible;
        UpdateStreetViewPanel();
    }

    private void UpdateStreetViewPanel()
    {
        StreetViewPanel.IsVisible = _isStreetViewVisible;
        StreetViewToggleButton.Text = _isStreetViewVisible ? "Hide street view" : "Show street view";

        if (_isStreetViewVisible)
        {
            RefreshStreetView();
        }
    }

    private void RefreshStreetView()
    {
        if (!_isStreetViewVisible)
        {
            return;
        }

        if (_streetViewLocation is not Location location)
        {
            ShowStreetViewMessage("Search for a place or tap the map to preview Street View here.");
            return;
        }

        var apiKey = _apiKeys.GoogleMapsApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = _apiKeys.GooglePlacesApiKey;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowStreetViewMessage("Add a Google Maps or Places API key to view Street View.");
            return;
        }

        var html = BuildStreetViewHtml(apiKey, location.Latitude, location.Longitude);
        StreetViewWebView.Source = new HtmlWebViewSource { Html = html };
        StreetViewWebView.IsVisible = true;
        StreetViewStatusLabel.IsVisible = false;
    }

    private void ShowStreetViewMessage(string message)
    {
        StreetViewWebView.Source = null;
        StreetViewWebView.IsVisible = false;
        StreetViewStatusLabel.Text = message;
        StreetViewStatusLabel.IsVisible = true;
    }

    private void SetStreetViewLocation(Location location)
    {
        _streetViewLocation = new Location(location.Latitude, location.Longitude);

        if (_isStreetViewVisible)
        {
            RefreshStreetView();
        }
    }

    private static string BuildStreetViewHtml(string apiKey, double latitude, double longitude)
    {
        var encodedKey = Uri.EscapeDataString(apiKey);
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lng = longitude.ToString(CultureInfo.InvariantCulture);

        return $@"<!DOCTYPE html>
<html>
<head>
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<style>
html, body {{ margin: 0; padding: 0; background-color: transparent; }}
iframe {{ border: 0; width: 100%; height: 100%; border-radius: 12px; }}
</style>
</head>
<body>
<iframe allowfullscreen
        loading=""lazy""
        referrerpolicy=""no-referrer-when-downgrade""
        src=""https://www.google.com/maps/embed/v1/streetview?key={encodedKey}&location={lat},{lng}&heading=210&pitch=0&fov=90""
></iframe>
</body>
</html>";
    }

    private async void OnClearStoriesClicked(object? sender, EventArgs e)
    {
        var saved = LoadSavedBuildings();
        if (saved.Count == 0)
        {
            await DisplayAlert("No stories", "You haven't generated any stories yet.", "OK");
            return;
        }

        var confirm = await DisplayAlert(
            "Remove all stories?",
            "This will remove every building story you've generated so far.",
            "Remove",
            "Cancel");

        if (!confirm)
        {
            return;
        }

        ClearSavedBuildings();
        RefreshPins(_allPlaces);

        await DisplayAlert("Stories removed", "All generated stories have been removed.", "OK");
    }

    // --- Select building flow ---

    private async void OnSelectBuildingClicked(object? sender, EventArgs e)
    {
        _isSelectingBuilding = !_isSelectingBuilding;
        SelectBuildingBtn.Text = _isSelectingBuilding ? "Cancel selecting" : "Select building";

        if (_isSelectingBuilding)
            await DisplayAlert("Select building",
                "Tap a building on the map. We'll reverse-geocode it and save it for your story.",
                "OK");
    }

    private async void OnMapClicked(object? sender, MapClickedEventArgs e)
    {
        var loc = e.Location;
        SetStreetViewLocation(loc);

        if (!_isSelectingBuilding) return;

        try
        {
            var place = await ReverseGeocodeAsync(loc.Latitude, loc.Longitude);
            if (place is null)
            {
                await DisplayAlert("No building found",
                    "Couldn’t find a nearby place for that point. Try tapping closer to a building outline.",
                    "OK");
                return;
            }

            var (placeId, name, address, lat, lng) = place.Value;
            var displayAddress = AddressFormatter.GetDisplayAddress(address) ?? address;

            SetStreetViewLocation(new Location(lat, lng));

            var confirm = await DisplayAlert("Save this building?",
                $"{name}\n{displayAddress}", "Save", "Cancel");
            if (!confirm) return;

            // Save locally
            var b = new SavedBuilding
            {
                PlaceId = placeId,
                Name = name,
                Address = address,
                Latitude = lat,
                Longitude = lng,
                SavedAtUtc = DateTime.UtcNow
            };
            SaveBuilding(b);

            // Show a pin immediately
            Map.Pins.Add(CreateStoryPin(placeId, name, address, new Location(lat, lng), isSaved: true));

            _isSelectingBuilding = false;
            SelectBuildingBtn.Text = "Select building";

            // Open the empty canvas page
            await NavigateToStoryCanvasAsync(b.PlaceId, b.Name, b.Address, latitude: b.Latitude, longitude: b.Longitude);

        }
        catch (Exception ex)
        {
            await DisplayAlert("Selection error", ex.Message, "OK");
        }
    }

    // --- Google Places helpers ---

    private string GetGooglePlacesKey()
    {
        var key = _apiKeys.GooglePlacesApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Google Places API key is not configured. Copy Resources/Raw/api_keys.example.json to api_keys.json and add your key.");
        }

        return key;
    }

    private async Task TryGoogleSearchAsync(string input)
    {
        try
        {
            var placeId = await GetFirstPlaceIdAsync(input);
            if (placeId is null)
            {
                await DisplayAlert("No results", "Google didn’t return any matches.", "OK");
                return;
            }

            await TryPlaceIdAsync(placeId);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Search error", ex.Message, "OK");
        }
    }

    private async Task TryPlaceIdAsync(string placeId)
    {
        try
        {
            var place = await GetPlaceDetailsAsync(placeId);
            if (place is null)
            {
                await DisplayAlert("Details error", "Could not load place details.", "OK");
                return;
            }

            var loc = new Location(place.Value.Lat, place.Value.Lng);
            Map.MoveToRegion(MapSpan.FromCenterAndRadius(loc, Distance.FromKilometers(1)));
            SetStreetViewLocation(loc);

            var pin = CreateStoryPin(placeId, place.Value.Name, place.Value.Address, loc);
            Map.Pins.Add(pin);

            await NavigateToStoryCanvasAsync(placeId, place.Value.Name, place.Value.Address,
                latitude: place.Value.Lat, longitude: place.Value.Lng);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Details error", ex.Message, "OK");
        }
    }

    private Pin CreateStoryPin(string placeId, string buildingName, string? buildingAddress, Location location, bool isSaved = false)
    {
        var parsedAddress = AddressFormatter.Parse(buildingAddress);
        var displayAddress = parsedAddress.DisplayAddress ?? parsedAddress.Original ?? string.Empty;

        var pin = new Pin
        {
            Label = isSaved ? $"★ {buildingName}" : buildingName,
            Address = displayAddress,
            Type = PinType.Place,
            Location = location
        };

        pin.MarkerClicked += async (s, e) =>
        {
            e.HideInfoWindow = true;
            SetStreetViewLocation(pin.Location);
            await NavigateToStoryCanvasAsync(placeId, buildingName, buildingAddress, parsedAddress,
                latitude: pin.Location.Latitude, longitude: pin.Location.Longitude);
        };

        return pin;
    }

    private Task NavigateToStoryCanvasAsync(string placeId, string buildingName, string? buildingAddress,
        AddressFormatter.ParsedAddress? parsed = null, double? latitude = null, double? longitude = null)
    {
        parsed ??= AddressFormatter.Parse(buildingAddress);

        var displayAddress = parsed.DisplayAddress ?? parsed.Original;
        var storyAddress = parsed.StoryAddress ?? parsed.Original;
        var place = _service.GetById(placeId);
        var buildingFacts = place?.Description;

        var lat = latitude;
        var lng = longitude;
        if (place is not null)
        {
            lat ??= place.Latitude;
            lng ??= place.Longitude;
        }

        return Navigation.PushModalAsync(new StoryCanvasPage(
            placeId,
            buildingName,
            displayAddress,
            storyAddress,
            buildingFacts,
            _storyService,
            _wikipediaService,
            lat,
            lng));
    }

    private async Task<string?> GetFirstPlaceIdAsync(string input)
    {
        var items = await GetAutocompleteAsync(input);
        return items.FirstOrDefault()?.PlaceId;
    }

    private async Task<List<SuggestionItem>> GetAutocompleteAsync(string input)
    {
        var results = new List<SuggestionItem>();
        if (string.IsNullOrWhiteSpace(input)) return results;

        var url = "https://places.googleapis.com/v1/places:autocomplete";

        var apiKey = GetGooglePlacesKey();

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("X-Goog-Api-Key", apiKey);
        req.Headers.Add("X-Goog-FieldMask",
            "suggestions.placePrediction.placeId,suggestions.placePrediction.text");

        var body = new
        {
            input = input,
            locationBias = new
            {
                rectangle = new
                {
                    low = new { latitude = 48.10, longitude = 16.20 },
                    high = new { latitude = 48.35, longitude = 16.55 }
                }
            }
        };

        req.Content = new StringContent(JsonSerializer.Serialize(body),
            System.Text.Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            return results; // Alerts are shown on explicit searches; here we fail quietly

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("suggestions", out var sugg) || sugg.GetArrayLength() == 0)
            return results;

        foreach (var s in sugg.EnumerateArray())
        {
            if (!s.TryGetProperty("placePrediction", out var pp)) continue;

            var placeId = pp.GetProperty("placeId").GetString() ?? "";
            string primary = "", secondary = "";

            if (pp.TryGetProperty("text", out var t))
            {
                primary = t.TryGetProperty("text", out var txt) ? (txt.GetString() ?? "") : "";
            }

            if (string.IsNullOrWhiteSpace(primary)) continue;

            results.Add(new SuggestionItem
            {
                PlaceId = placeId,
                PrimaryText = primary,
                SecondaryText = secondary
            });
        }

        return results;
    }

    private async Task<(string Name, string Address, double Lat, double Lng)?> GetPlaceDetailsAsync(string placeId)
    {
        var apiKey = GetGooglePlacesKey();
        var url = $"https://places.googleapis.com/v1/places/{placeId}?fields=location,displayName,formattedAddress&key={Uri.EscapeDataString(apiKey)}";
        var json = await _http.GetStringAsync(url);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("location", out var loc)) return null;
        var lat = loc.GetProperty("latitude").GetDouble();
        var lng = loc.GetProperty("longitude").GetDouble();

        var name = root.TryGetProperty("displayName", out var dn) && dn.TryGetProperty("text", out var txt)
            ? txt.GetString() ?? "(Place)"
            : "(Place)";

        var address = root.TryGetProperty("formattedAddress", out var addr)
            ? addr.GetString() ?? ""
            : "";

        return (name, address, lat, lng);
    }

    // Reverse-geocode a tapped point to the nearest place/building
    // Find the nearest place to the tapped point (substitute for reverse geocode)
    // Find the nearest place to the tapped point (Places API v1 "New")
    private async Task<(string PlaceId, string Name, string Address, double Lat, double Lng)?> ReverseGeocodeAsync(double lat, double lng)
    {
        var url = "https://places.googleapis.com/v1/places:searchNearby";

        var apiKey = GetGooglePlacesKey();

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("X-Goog-Api-Key", apiKey);
        req.Headers.Add("X-Goog-FieldMask", "places.id,places.displayName,places.formattedAddress,places.location");

        var body = new
        {
            locationRestriction = new
            {
                circle = new
                {
                    center = new { latitude = lat, longitude = lng },
                    radius = 50.0  // meters; tweak as needed
                }
            },
            rankPreference = "DISTANCE",
            maxResultCount = 1,
            languageCode = "en",
            regionCode = "AT"
            // ⛔ No includedTypes here — legacy types like "establishment" will 400
        };

        req.Content = new StringContent(JsonSerializer.Serialize(body),
            System.Text.Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req);
        var payload = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Nearby search failed {(int)resp.StatusCode}: {payload}");

        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("places", out var arr) || arr.GetArrayLength() == 0)
            return null;

        var p = arr[0];
        var id = p.GetProperty("id").GetString() ?? "";
        var name = p.TryGetProperty("displayName", out var dn) && dn.TryGetProperty("text", out var txt)
            ? txt.GetString() ?? "(Place)" : "(Place)";
        var address = p.TryGetProperty("formattedAddress", out var a) ? a.GetString() ?? "" : "";
        var loc = p.GetProperty("location");
        var plat = loc.GetProperty("latitude").GetDouble();
        var plng = loc.GetProperty("longitude").GetDouble();

        return (id, name, address, plat, plng);
    }


    // --- Persistence of saved buildings ---

    private void SaveBuilding(SavedBuilding b)
    {
        var list = LoadSavedBuildings();
        list.Add(b);
        var json = JsonSerializer.Serialize(list);
        Preferences.Set(SavedBuildingsKey, json);
    }

    private void ClearSavedBuildings()
    {
        Preferences.Remove(SavedBuildingsKey);
    }

    private List<SavedBuilding> LoadSavedBuildings()
    {
        var json = Preferences.Get(SavedBuildingsKey, "");
        if (string.IsNullOrWhiteSpace(json)) return new List<SavedBuilding>();
        try { return JsonSerializer.Deserialize<List<SavedBuilding>>(json) ?? new List<SavedBuilding>(); }
        catch { return new List<SavedBuilding>(); }
    }

    // DTOs
    private sealed class SuggestionItem
    {
        public string PlaceId { get; set; } = "";
        public string PrimaryText { get; set; } = "";
        public string SecondaryText { get; set; } = "";
    }

    private sealed class SavedBuilding
    {
        public string PlaceId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime SavedAtUtc { get; set; }
    }
}
}
