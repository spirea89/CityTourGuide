using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using CityTour.Models;
using CityTour.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using System.Linq;
using System.Reflection;

namespace CityTour;

public partial class StoryCanvasPage : ContentPage
{
    private readonly string _placeId;
    private readonly string _buildingName;
    private readonly string? _displayAddress;
    private readonly string? _storyAddress;
    private readonly IAiStoryService _storyService;
    private readonly string? _buildingFacts;
    private readonly string _preferredLanguage;
    private CancellationTokenSource? _generationCts;
    private bool _hasTriggeredInitialGeneration;
    private bool _isInitializingCategory;
    private StoryCategory _selectedCategory;
    private readonly List<StoryCategoryOption> _categoryOptions = new()
    {
        new("History", StoryCategory.History),
        new("Personalities", StoryCategory.Personalities),
        new("Architecture", StoryCategory.Architecture),
        new("Today", StoryCategory.Today),
        new("Kids", StoryCategory.Kids)
    };
    private CancellationTokenSource? _speechCts;
    private bool _isSpeaking;
    private bool _isLoadingVoices;
    private bool _hasAttemptedVoiceLoad;
    private List<LocaleOption> _voiceOptions = new();
    private readonly ObservableCollection<ChatMessage> _chatMessages = new();
    private CancellationTokenSource? _chatCts;
    private bool _isChatBusy;
    private const string ChatReadyStatusMessage = "Ask the guide for more details about this address.";
    private const string ChatBusyStatusMessage = "Asking the tour guide…";
    private const string ChatFollowUpStatusMessage = "Ask another follow-up question whenever you're curious.";

    public StoryCanvasPage(
        string placeId,
        string buildingName,
        string? displayAddress,
        string? storyAddress,
        string? buildingFacts,
        IAiStoryService storyService)
    {
        InitializeComponent();
        _placeId = placeId;
        _buildingName = buildingName;
        _displayAddress = string.IsNullOrWhiteSpace(displayAddress) ? null : displayAddress;
        _storyAddress = string.IsNullOrWhiteSpace(storyAddress) ? null : storyAddress;
        _storyService = storyService;
        _buildingFacts = string.IsNullOrWhiteSpace(buildingFacts) ? null : buildingFacts.Trim();
        _preferredLanguage = DeterminePreferredLanguage();

        ConfigureCategoryPicker();

        var addressForStory = string.IsNullOrWhiteSpace(_storyAddress)
            ? _displayAddress
            : _storyAddress;

        if (string.IsNullOrWhiteSpace(addressForStory))
        {
            addressForStory = buildingName;
        }

        BuildingNameLabel.Text = addressForStory;

        StatusLabel.Text = $"Preparing {GetCategoryDisplayName(_selectedCategory)} story…";
        RegenerateButton.IsEnabled = false;
        UpdateRegenerateButtonText();
        ChatCollectionView.ItemsSource = _chatMessages;
        ChatStatusLabel.Text = ChatReadyStatusMessage;
        UpdatePromptPreview();

        StoryEditor.TextChanged += OnStoryTextChanged;
        UpdateAudioControls();
        UpdateChatControls();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = EnsureVoiceOptionsAsync();
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
        CancelSpeech();
        _chatCts?.Cancel();
    }

    private async Task GenerateStoryAsync(bool userInitiated = false)
    {
        _generationCts?.Cancel();
        var cts = new CancellationTokenSource();
        _generationCts = cts;

        CancelSpeech();

        var category = _selectedCategory;
        var categoryLabel = GetCategoryDisplayName(category);

        try
        {
            await ToggleLoadingAsync(true, userInitiated, categoryLabel);

            var addressForStory = GetAddressForStory();
            var storyResult = await _storyService.GenerateStoryAsync(
                _buildingName,
                addressForStory,
                category,
                _buildingFacts,
                _preferredLanguage,
                cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StoryEditor.Text = storyResult.Story;
                PromptLabel.Text = storyResult.Prompt;
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
        CancelSpeech();
        _chatCts?.Cancel();
        await Navigation.PopModalAsync();
    }

    private async Task EnsureVoiceOptionsAsync()
    {
        if (_hasAttemptedVoiceLoad || _isLoadingVoices)
        {
            return;
        }

        _isLoadingVoices = true;

        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();
            var options = locales
                .OrderBy(locale => GetLocaleSortKey(locale), StringComparer.OrdinalIgnoreCase)
                .ThenBy(locale => GetLocaleName(locale) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(locale => new LocaleOption(locale))
                .ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _voiceOptions = options;
                VoicePicker.ItemsSource = _voiceOptions;
                if (_voiceOptions.Count > 0)
                {
                    VoicePicker.SelectedIndex = 0;
                }

                UpdateAudioControls();
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Text-to-speech unavailable", ex.Message, "OK");
            });
        }
        finally
        {
            _hasAttemptedVoiceLoad = true;
            _isLoadingVoices = false;
        }
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

        var previousCategory = _selectedCategory;
        _selectedCategory = option.Category;
        UpdateRegenerateButtonText();

        var categoryChanged = previousCategory != _selectedCategory;
        if (categoryChanged)
        {
            UpdatePromptPreview();
        }

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

    private void UpdatePromptPreview()
    {
        var prompt = _storyService.BuildStoryPrompt(
            _buildingName,
            GetAddressForStory(),
            _selectedCategory,
            _buildingFacts,
            _preferredLanguage);
        PromptLabel.Text = prompt;
    }

    private void OnSendChatClicked(object? sender, EventArgs e)
    {
        _ = SendChatAsync();
    }

    private void OnChatEntryCompleted(object? sender, EventArgs e)
    {
        _ = SendChatAsync();
    }

    private void OnChatTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateChatControls();
    }

    private async Task SendChatAsync()
    {
        if (_isChatBusy)
        {
            return;
        }

        var question = ChatEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            return;
        }

        ChatEntry.Text = string.Empty;
        UpdateChatControls();

        await AddChatMessageAsync(new ChatMessage(question, isUser: true));

        var cts = new CancellationTokenSource();
        _chatCts?.Cancel();
        _chatCts = cts;

        try
        {
            await SetChatBusyStateAsync(true, ChatBusyStatusMessage);

            var address = GetAddressForStory();
            var storyText = string.IsNullOrWhiteSpace(StoryEditor.Text) ? null : StoryEditor.Text;

            var response = await _storyService.AskAddressDetailsAsync(
                _buildingName,
                address,
                storyText,
                question,
                cts.Token);

            cts.Token.ThrowIfCancellationRequested();

            await AddChatMessageAsync(new ChatMessage(response, isUser: false));
            await SetChatBusyStateAsync(false, ChatFollowUpStatusMessage);
        }
        catch (OperationCanceledException)
        {
            await SetChatBusyStateAsync(false, ChatReadyStatusMessage);
        }
        catch (Exception ex)
        {
            await SetChatBusyStateAsync(false, $"Could not ask for more details. {ex.Message}");
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Chat failed", ex.Message, "OK");
            });
        }
        finally
        {
            if (ReferenceEquals(_chatCts, cts))
            {
                _chatCts = null;
            }

            cts.Dispose();
        }
    }

    private Task AddChatMessageAsync(ChatMessage message)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            _chatMessages.Add(message);
            ChatCollectionView.ScrollTo(message, position: ScrollToPosition.End, animate: true);
        });
    }

    private Task SetChatBusyStateAsync(bool isBusy, string message)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            _isChatBusy = isBusy;
            ChatLoadingIndicator.IsVisible = isBusy;
            ChatLoadingIndicator.IsRunning = isBusy;
            ChatStatusLabel.Text = message;
            UpdateChatControls();
        });
    }

    private void UpdateChatControls()
    {
        var hasText = !string.IsNullOrWhiteSpace(ChatEntry.Text);
        ChatEntry.IsEnabled = !_isChatBusy;
        SendChatButton.IsEnabled = !_isChatBusy && hasText;
    }

    private void OnStoryTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateAudioControls();
    }

    private async void OnListenClicked(object? sender, EventArgs e)
    {
        var text = StoryEditor.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            await DisplayAlert("Story unavailable", "There's no story to read right now.", "OK");
            return;
        }

        SpeechOptions? options = null;
        var locale = GetSelectedLocale();
        if (locale is not null)
        {
            options = new SpeechOptions
            {
                Locale = locale
            };
        }

        CancelSpeech();

        var cts = new CancellationTokenSource();
        _speechCts = cts;

        try
        {
            await SetIsSpeakingAsync(true);
            await TextToSpeech.Default.SpeakAsync(text, options, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Text-to-speech failed", ex.Message, "OK");
            });
        }
        finally
        {
            if (ReferenceEquals(_speechCts, cts))
            {
                _speechCts = null;
                await SetIsSpeakingAsync(false);
            }

            cts.Dispose();
        }
    }

    private void OnStopClicked(object? sender, EventArgs e)
    {
        CancelSpeech();
    }

    private Task SetIsSpeakingAsync(bool isSpeaking)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            _isSpeaking = isSpeaking;
            UpdateAudioControls();
        });
    }

    private void UpdateAudioControls()
    {
        var hasText = !string.IsNullOrWhiteSpace(StoryEditor.Text);
        ListenButton.IsEnabled = !_isSpeaking && hasText;
        StopButton.IsEnabled = _isSpeaking;
        VoicePicker.IsEnabled = !_isSpeaking && _voiceOptions.Count > 0;
    }

    private void CancelSpeech()
    {
        if (_speechCts is null)
        {
            return;
        }

        if (!_speechCts.IsCancellationRequested)
        {
            _speechCts.Cancel();
        }
    }

    private Locale? GetSelectedLocale()
    {
        return VoicePicker.SelectedItem is LocaleOption option ? option.Locale : null;
    }

    private static string GetLocaleSortKey(Locale locale)
    {
        return GetLocaleDisplayName(locale)
            ?? GetLocaleName(locale)
            ?? GetLocaleLanguage(locale)
            ?? locale.ToString()
            ?? string.Empty;
    }

    private static string? GetLocaleDisplayName(Locale locale)
    {
        return GetLocalePropertyValue(locale, "DisplayName", "Description", "Label");
    }

    private static string? GetLocaleName(Locale locale)
    {
        return GetLocalePropertyValue(locale, "Name", "LocaleName", "Identifier", "Id");
    }

    private static string? GetLocaleLanguage(Locale locale)
    {
        return GetLocalePropertyValue(locale, "Language", "LanguageCode");
    }

    private static string? GetLocalePropertyValue(Locale locale, params string[] propertyNames)
    {
        var localeType = locale.GetType();
        foreach (var propertyName in propertyNames)
        {
            var property = localeType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property?.GetValue(locale) is { } value)
            {
                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private string? GetAddressForStory()
    {
        var address = string.IsNullOrWhiteSpace(_storyAddress) ? _displayAddress : _storyAddress;
        return string.IsNullOrWhiteSpace(address) ? null : address;
    }

    private static string DeterminePreferredLanguage()
    {
        try
        {
            var culture = CultureInfo.CurrentUICulture ?? CultureInfo.CurrentCulture;
            var language = culture.EnglishName;

            if (string.IsNullOrWhiteSpace(language))
            {
                language = culture.DisplayName;
            }

            return string.IsNullOrWhiteSpace(language) ? "English" : language;
        }
        catch
        {
            return "English";
        }
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

    private sealed class LocaleOption
    {
        public LocaleOption(Locale locale)
        {
            Locale = locale;
            DisplayName = BuildDisplayName(locale);
        }

        public Locale Locale { get; }
        public string DisplayName { get; }

        private static string BuildDisplayName(Locale locale)
        {
            var display = GetLocaleDisplayName(locale);
            var name = GetLocaleName(locale);

            if (string.IsNullOrWhiteSpace(display))
            {
                var language = GetLocaleLanguage(locale);
                return string.IsNullOrWhiteSpace(name)
                    ? string.IsNullOrWhiteSpace(language) ? "Unknown" : language!
                    : name!;
            }

            return string.IsNullOrWhiteSpace(name)
                ? display!
                : $"{display} ({name})";
        }
    }
}
