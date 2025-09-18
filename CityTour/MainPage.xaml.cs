using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using CityTour.Services;
using CityTour.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage; // Preferences

namespace CityTour;

public partial class MainPage : ContentPage
{
    private readonly PlaceService _service;
    private readonly IAiStoryService _storyService;
    private List<Place> _allPlaces = new();

    // Google Places (Web)
    private readonly HttpClient _http = new();
    private const string GooglePlacesKey = "AIzaSyD1K-t8tsPgwbQUD888Xh9kQDT5w6sWIfc";

    // Live suggestions
    private CancellationTokenSource? _typeCts;

    // Building selection mode
    private bool _isSelectingBuilding = false;
    private const string SavedBuildingsKey = "citytour.saved_buildings";

    public MainPage(PlaceService service, IAiStoryService storyService)
    {
        InitializeComponent();
        _service = service;
        _storyService = storyService;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Load local places
        _allPlaces = _service.GetAll().ToList();

        // Center on Vienna
        var vienna = new Location(48.2082, 16.3738);
        Map.MoveToRegion(MapSpan.FromCenterAndRadius(vienna, Distance.FromKilometers(3)));

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
            var pin = new Pin
            {
                Label = p.Name,
                Address = p.Address,
                Type = PinType.Place,
                Location = new Location(p.Latitude, p.Longitude)
            };

            pin.MarkerClicked += async (s, e) =>
            {
                e.HideInfoWindow = true;
                await Shell.Current.GoToAsync($"DetailPage?id={p.Id}");
            };

            Map.Pins.Add(pin);
        }

        // Also show saved buildings
        foreach (var b in LoadSavedBuildings())
        {
            Map.Pins.Add(new Pin
            {
                Label = $"★ {b.Name}",
                Address = b.Address,
                Type = PinType.Place,
                Location = new Location(b.Latitude, b.Longitude)
            });
        }
    }

    private void ApplyFilter(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            RefreshPins(_allPlaces);
            return;
        }

        var filtered = _allPlaces
            .Where(p => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToList();

        RefreshPins(filtered);
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

        var items = await GetAutocompleteAsync(input);
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
        if (!_isSelectingBuilding) return;

        var loc = e.Location;
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

            var confirm = await DisplayAlert("Save this building?",
                $"{name}\n{address}", "Save", "Cancel");
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
            Map.Pins.Add(new Pin
            {
                Label = $"★ {name}",
                Address = address,
                Type = PinType.Place,
                Location = new Location(lat, lng)
            });

            _isSelectingBuilding = false;
            SelectBuildingBtn.Text = "Select building";

            // Optional confirmation
            await DisplayAlert("Saved", "Building saved. Opening the story canvas…", "OK");

            // Open the empty canvas page
            await Navigation.PushModalAsync(new StoryCanvasPage(b.PlaceId, b.Name, b.Address, _storyService));

        }
        catch (Exception ex)
        {
            await DisplayAlert("Selection error", ex.Message, "OK");
        }
    }

    // --- Google Places helpers ---

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

            Map.Pins.Add(new Pin
            {
                Label = place.Value.Name,
                Address = place.Value.Address,
                Type = PinType.Place,
                Location = loc
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Details error", ex.Message, "OK");
        }
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

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("X-Goog-Api-Key", GooglePlacesKey);
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
        var url = $"https://places.googleapis.com/v1/places/{placeId}?fields=location,displayName,formattedAddress&key={GooglePlacesKey}";
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

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("X-Goog-Api-Key", GooglePlacesKey);
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
