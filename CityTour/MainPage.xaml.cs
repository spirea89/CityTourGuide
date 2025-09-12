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

namespace CityTour;

public partial class MainPage : ContentPage
{
    private readonly PlaceService _service;
    private List<Place> _allPlaces = new();

    // Google Places (Web) — simple, cross-platform
    private readonly HttpClient _http = new();
    private const string GooglePlacesKey = "AIzaSyD1K-t8tsPgwbQUD888Xh9kQDT5w6sWIfc";

    // Live suggestions
    private readonly List<SuggestionItem> _suggestions = new();
    private CancellationTokenSource? _typeCts;

    public MainPage(PlaceService service)
    {
        InitializeComponent();
        _service = service;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Load places once
        _allPlaces = _service.GetAll().ToList();

        // Center on Vienna
        var vienna = new Location(48.2082, 16.3738);
        Map.MoveToRegion(MapSpan.FromCenterAndRadius(vienna, Distance.FromKilometers(3)));

        // Draw all pins initially
        RefreshPins(_allPlaces);
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
        // Keep your local filtering
        ApplyFilter(e.NewTextValue);

        // Debounced Google autocomplete
        _typeCts?.Cancel();
        _typeCts = new CancellationTokenSource();
        var token = _typeCts.Token;

        _ = DebouncedSuggestionsAsync(e.NewTextValue, token);
    }

    private async Task DebouncedSuggestionsAsync(string? input, CancellationToken ct)
    {
        try
        {
            await Task.Delay(250, ct); // debounce
        }
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

    // Pressing the Search button centers & pins the best Google result
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

        // Also try Google Places; if it finds a result, center & pin it
        await TryGoogleSearchAsync(query);
    }

    // List selection -> center & pin that exact place
    private async void OnSuggestionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.FirstOrDefault() is not SuggestionItem item)
            return;

        ResultsList.SelectedItem = null;
        ResultsList.IsVisible = false;
        Search.Text = item.PrimaryText;

        await TryPlaceIdAsync(item.PlaceId);
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
            // Bias to Vienna area; adjust if you like
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
        {
            var err = await resp.Content.ReadAsStringAsync();
            await DisplayAlert("Google error",
                $"HTTP {(int)resp.StatusCode}\n{err}", "OK");
            return results;
        }

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
                secondary = t.TryGetProperty("matches", out var _) ? "" : "";
            }

            // If primary empty, skip
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

    private void OnRecenterClicked(object sender, EventArgs e)
    {
        var vienna = new Location(48.2082, 16.3738);
        Map.MoveToRegion(MapSpan.FromCenterAndRadius(vienna, Distance.FromKilometers(3)));
    }

    // Simple DTO for suggestions
    private sealed class SuggestionItem
    {
        public string PlaceId { get; set; } = "";
        public string PrimaryText { get; set; } = "";
        public string SecondaryText { get; set; } = "";
    }
}
