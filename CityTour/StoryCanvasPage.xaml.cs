using System.Collections.Generic;
using CityTour.Models;
using CityTour.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace CityTour;

public partial class StoryCanvasPage : ContentPage
{
    private readonly string _placeId;
    private readonly string _buildingName;
    private readonly string? _buildingAddress;
    private readonly IAiStoryService _storyService;
    private CancellationTokenSource? _generationCts;
    private bool _hasTriggeredInitialGeneration;
    private bool _isInitializingCategory;
    private StoryCategory _selectedCategory;
    private readonly List<StoryCategoryOption> _categoryOptions = new()
    {
        new("History", StoryCategory.History),
        new("Personalities", StoryCategory.Personalities),
        new("Architecture", StoryCategory.Architecture),
        new("Kids", StoryCategory.Kids)
    };

    public StoryCanvasPage(string placeId, string buildingName, string buildingAddress, IAiStoryService storyService)
    {
        InitializeComponent();
        _placeId = placeId;
        _buildingName = buildingName;
        _buildingAddress = string.IsNullOrWhiteSpace(buildingAddress) ? null : buildingAddress;
        _storyService = storyService;

        ConfigureCategoryPicker();

        var labelText = string.IsNullOrWhiteSpace(_buildingAddress)
            ? buildingName
            : $"{buildingName}\n{_buildingAddress}";
        BuildingNameLabel.Text = labelText;

        StatusLabel.Text = $"Preparing {GetCategoryDisplayName(_selectedCategory)} story…";
        RegenerateButton.IsEnabled = false;
        UpdateRegenerateButtonText();
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

        var category = _selectedCategory;
        var categoryLabel = GetCategoryDisplayName(category);

        try
        {
            await ToggleLoadingAsync(true, userInitiated, categoryLabel);

            var story = await _storyService.GenerateStoryAsync(_buildingName, _buildingAddress, category, cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StoryEditor.Text = story;
            });

            await SetStatusAsync($"Story generated with AI ({categoryLabel} focus). Feel free to tweak or add your own notes.");
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
            await ToggleLoadingAsync(false, userInitiated, categoryLabel);
        }
    }

    private Task ToggleLoadingAsync(bool isLoading, bool userInitiated, string categoryLabel)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            LoadingIndicator.IsVisible = isLoading;
            LoadingIndicator.IsRunning = isLoading;
            RegenerateButton.IsEnabled = !isLoading;
            if (isLoading)
            {
                var verb = userInitiated ? "Regenerating" : "Generating";
                StatusLabel.Text = $"{verb} {categoryLabel} story…";
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

    private void ConfigureCategoryPicker()
    {
        if (_categoryOptions.Count == 0)
        {
            return;
        }

        _isInitializingCategory = true;
        CategoryPicker.ItemsSource = _categoryOptions;
        CategoryPicker.ItemDisplayBinding = new Binding(nameof(StoryCategoryOption.DisplayName));
        _selectedCategory = _categoryOptions[0].Category;
        CategoryPicker.SelectedIndex = 0;
        _isInitializingCategory = false;
    }

    private void OnCategoryChanged(object? sender, EventArgs e)
    {
        if (CategoryPicker.SelectedItem is not StoryCategoryOption option)
        {
            return;
        }

        var categoryChanged = option.Category != _selectedCategory;
        _selectedCategory = option.Category;
        UpdateRegenerateButtonText();

        if (!categoryChanged || _isInitializingCategory)
        {
            return;
        }

        var userInitiated = _hasTriggeredInitialGeneration;
        _ = GenerateStoryAsync(userInitiated: userInitiated);
    }

    private void UpdateRegenerateButtonText()
    {
        var label = GetCategoryDisplayName(_selectedCategory);
        RegenerateButton.Text = $"Regenerate {label} story";
    }

    private string GetCategoryDisplayName(StoryCategory category)
    {
        foreach (var option in _categoryOptions)
        {
            if (option.Category == category)
            {
                return option.DisplayName;
            }
        }

        return category.ToString();
    }

    private sealed class StoryCategoryOption
    {
        public StoryCategoryOption(string displayName, StoryCategory category)
        {
            DisplayName = displayName;
            Category = category;
        }

        public string DisplayName { get; }
        public StoryCategory Category { get; }
    }
}
