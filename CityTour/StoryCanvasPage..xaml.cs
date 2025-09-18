using CityTour.Services;
using Microsoft.Maui.ApplicationModel;

namespace CityTour;

public partial class StoryCanvasPage : ContentPage
{
    private readonly string _placeId;
    private readonly string _buildingName;
    private readonly string? _buildingAddress;
    private readonly IAiStoryService _storyService;
    private CancellationTokenSource? _generationCts;
    private bool _hasTriggeredInitialGeneration;

    public StoryCanvasPage(string placeId, string buildingName, string buildingAddress, IAiStoryService storyService)
    {
        InitializeComponent();
        _placeId = placeId;
        _buildingName = buildingName;
        _buildingAddress = string.IsNullOrWhiteSpace(buildingAddress) ? null : buildingAddress;
        _storyService = storyService;

        var labelText = string.IsNullOrWhiteSpace(_buildingAddress)
            ? buildingName
            : $"{buildingName}\n{_buildingAddress}";
        BuildingNameLabel.Text = labelText;

        StatusLabel.Text = "Preparing AI story…";
        RegenerateButton.IsEnabled = false;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_hasTriggeredInitialGeneration)
        {
            return;
        }

        _hasTriggeredInitialGeneration = true;
        _ = GenerateStoryAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _generationCts?.Cancel();
    }

    private async Task GenerateStoryAsync(bool userInitiated = false)
    {
        _generationCts?.Cancel();
        var cts = new CancellationTokenSource();
        _generationCts = cts;

        try
        {
            await ToggleLoadingAsync(true, userInitiated);

            var story = await _storyService.GenerateStoryAsync(_buildingName, _buildingAddress, cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StoryEditor.Text = story;
            });

            await SetStatusAsync("Story generated with AI. Feel free to tweak or add your own notes.");
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            await SetStatusAsync($"Could not generate story. {ex.Message}");

            if (userInitiated)
            {
                await DisplayAlert("Story generation failed", ex.Message, "OK");
            }
        }
        finally
        {
            await ToggleLoadingAsync(false, userInitiated);
        }
    }

    private Task ToggleLoadingAsync(bool isLoading, bool userInitiated)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            LoadingIndicator.IsVisible = isLoading;
            LoadingIndicator.IsRunning = isLoading;
            RegenerateButton.IsEnabled = !isLoading;
            if (isLoading)
            {
                StatusLabel.Text = userInitiated
                    ? "Regenerating story…"
                    : "Generating story with AI…";
            }
        });
    }

    private Task SetStatusAsync(string message)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            StatusLabel.Text = message;
        });
    }

    private void OnRegenerateClicked(object? sender, EventArgs e)
    {
        _ = GenerateStoryAsync(userInitiated: true);
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        _generationCts?.Cancel();
        await Navigation.PopModalAsync();
    }
}
