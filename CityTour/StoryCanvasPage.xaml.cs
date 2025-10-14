using System;
using System.Collections;
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
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace CityTour;

public partial class StoryCanvasPage : ContentPage
{
    private readonly string _placeId;
    private readonly string _buildingName;
    private readonly string? _displayAddress;
    private readonly string? _storyAddress;
    private readonly IAiStoryService _storyService;
    private readonly IApiKeyProvider _apiKeys;
    private readonly double? _latitude;
    private readonly double? _longitude;
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
    private readonly List<ModelOption> _modelOptions = new()
    {
        new("GPT-4o", "gpt-4o", "Balanced quality and cost (Recommended)"),
        new("GPT-4.1", "gpt-4.1", "Highest quality responses"),
        new("GPT-4.1 mini", "gpt-4.1-mini", "Smarter reasoning, moderate speed"),
        new("GPT-4o mini", "gpt-4o-mini", "Fast and cost-efficient"),
        new("GPT-5", "gpt-5", "Experimental - may not produce content")
    };
    private ModelOption? _selectedModel;
    private bool _isInitializingModel;
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
    private string? _lastRawOpenAiResponse;
    private bool _isPromptVisible;
    private FactCheckSummary? _currentFactCheck;

    public StoryCanvasPage(
        string placeId,
        string buildingName,
        string? displayAddress,
        string? storyAddress,
        string? buildingFacts,
        IAiStoryService storyService,
        IApiKeyProvider apiKeyProvider,
        double? latitude = null,
        double? longitude = null)
    {
        InitializeComponent();
        _placeId = placeId;
        _buildingName = buildingName;
        _displayAddress = string.IsNullOrWhiteSpace(displayAddress) ? null : displayAddress;
        _storyAddress = string.IsNullOrWhiteSpace(storyAddress) ? null : storyAddress;
        _storyService = storyService;
        _apiKeys = apiKeyProvider;
        _latitude = latitude;
        _longitude = longitude;
        _buildingFacts = string.IsNullOrWhiteSpace(buildingFacts) ? null : buildingFacts.Trim();
        _preferredLanguage = DeterminePreferredLanguage();

        ConfigureCategoryPicker();
        ConfigureModelPicker();

        var addressForStory = string.IsNullOrWhiteSpace(_storyAddress)
            ? _displayAddress
            : _storyAddress;

        if (string.IsNullOrWhiteSpace(addressForStory))
        {
            addressForStory = buildingName;
        }

        BuildingNameLabel.Text = addressForStory;

        StatusLabel.Text = $"Preparing {GetCategoryDisplayName(_selectedCategory)} story with {GetSelectedModelLabel()}…";
        RegenerateButton.IsEnabled = false;
        UpdateRegenerateButtonText();
        ChatCollectionView.ItemsSource = _chatMessages;
        ChatStatusLabel.Text = ChatReadyStatusMessage;
        SetPromptVisibility(false);
        UpdatePromptPreview();
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
        var modelLabel = GetSelectedModelLabel();

        try
        {
            await ToggleLoadingAsync(true, userInitiated, categoryLabel, modelLabel);

            var addressForStory = GetAddressForStory();
            var storyResult = await _storyService.GenerateStoryWithFactCheckAsync(
                _buildingName,
                addressForStory,
                category,
                _buildingFacts,
                _preferredLanguage,
                _latitude,
                _longitude,
                cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StoryLabel.Text = storyResult.Story;
                PromptLabel.Text = storyResult.Prompt;
                _currentFactCheck = storyResult.FactCheck;
                UpdateAudioControls();
                UpdateFactCheckDisplay();
            });

            await SetStatusAsync($"Story generated with {modelLabel} ({categoryLabel} focus).");
            UpdateRawResponse(null);
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            await SetStatusAsync($"Could not generate story. {ex.Message}");
            UpdateRawResponse(GetRawResponse(ex));

            if (userInitiated)
            {
                await DisplayAlert("Story generation failed", ex.Message, "OK");
            }
        }
        finally
        {
            await ToggleLoadingAsync(false, userInitiated, categoryLabel, modelLabel);
        }
    }

    private Task ToggleLoadingAsync(bool isLoading, bool userInitiated, string categoryLabel, string modelLabel)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            LoadingIndicator.IsVisible = isLoading;
            LoadingIndicator.IsRunning = isLoading;
            RegenerateButton.IsEnabled = !isLoading;
            if (isLoading)
            {
                var verb = userInitiated ? "Regenerating" : "Generating";
                StatusLabel.Text = $"{verb} {categoryLabel} story with {modelLabel}…";
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

    private void ConfigureModelPicker()
    {
        if (_modelOptions.Count == 0)
        {
            ModelPicker.IsEnabled = false;
            return;
        }

        _isInitializingModel = true;

        try
        {
            ModelPicker.ItemsSource = _modelOptions;

            var currentModel = _storyService.CurrentModel;
            var selectedOption = _modelOptions
                .FirstOrDefault(option => string.Equals(option.ModelId, currentModel, StringComparison.OrdinalIgnoreCase))
                ?? _modelOptions[0];

            _selectedModel = selectedOption;

            var index = _modelOptions.IndexOf(selectedOption);
            if (index >= 0)
            {
                ModelPicker.SelectedIndex = index;
            }
            ModelPicker.SelectedItem = selectedOption;

            if (!string.Equals(selectedOption.ModelId, currentModel, StringComparison.Ordinal))
            {
                _storyService.SetModel(selectedOption.ModelId);
            }

            ModelPicker.IsEnabled = true;
        }
        finally
        {
            _isInitializingModel = false;
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

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (ModelPicker.SelectedItem is not ModelOption option)
        {
            return;
        }

        if (_selectedModel is not null && string.Equals(_selectedModel.ModelId, option.ModelId, StringComparison.Ordinal))
        {
            return;
        }

        var previousModel = _selectedModel;
        _selectedModel = option;

        if (_isInitializingModel)
        {
            return;
        }

        try
        {
            _storyService.SetModel(option.ModelId);
        }
        catch (Exception ex)
        {
            _selectedModel = previousModel;
            _ = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                _isInitializingModel = true;
                if (previousModel is not null)
                {
                    ModelPicker.SelectedItem = previousModel;
                    var index = _modelOptions.IndexOf(previousModel);
                    if (index >= 0)
                    {
                        ModelPicker.SelectedIndex = index;
                    }
                }
                else
                {
                    ModelPicker.SelectedIndex = -1;
                    ModelPicker.SelectedItem = null;
                }
                _isInitializingModel = false;
                await DisplayAlert("Model selection failed", ex.Message, "OK");
            });
            return;
        }

        _ = GenerateStoryAsync(userInitiated: true);
    }

    private void UpdateRegenerateButtonText()
    {
        var label = GetCategoryDisplayName(_selectedCategory);
        RegenerateButton.Text = $"Regenerate {label} story";
    }

    private string GetSelectedModelLabel()
    {
        if (_selectedModel is not null)
        {
            return _selectedModel.Label;
        }

        var currentModel = _storyService.CurrentModel;
        if (!string.IsNullOrWhiteSpace(currentModel))
        {
            var option = _modelOptions.FirstOrDefault(o =>
                string.Equals(o.ModelId, currentModel, StringComparison.OrdinalIgnoreCase));
            if (option is not null)
            {
                _selectedModel = option;
                return option.Label;
            }

            return currentModel;
        }

        return "AI";
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

    private void OnTogglePromptClicked(object? sender, EventArgs e)
    {
        SetPromptVisibility(!_isPromptVisible);
    }

    private void SetPromptVisibility(bool isVisible)
    {
        _isPromptVisible = isVisible;
        PromptLabel.IsVisible = isVisible;
        TogglePromptButton.Text = isVisible ? "Hide prompt" : "Show prompt";
    }

    private void UpdateFactCheckDisplay()
    {
        if (_currentFactCheck == null)
        {
            // Hide fact-check button if no data
            FactCheckButton.IsVisible = false;
            return;
        }

        // Show fact-check button with updated text
        FactCheckButton.IsVisible = true;
        
        // Update button text with basic info
        var verifiedCount = _currentFactCheck.VerifiedFacts.Count;
        var unverifiedCount = _currentFactCheck.UnverifiedClaims.Count;
        
        if (verifiedCount > 0 || unverifiedCount > 0)
        {
            FactCheckButton.Text = $"📊 Fact check ({verifiedCount}✅ {unverifiedCount}❓)";
        }
        else
        {
            FactCheckButton.Text = "📊 View fact check";
        }

        // Change button color based on fact-check quality
        if (_currentFactCheck.HasMajorInaccuracies)
        {
            FactCheckButton.BackgroundColor = Color.FromArgb("#FFEBEE"); // Light red
            FactCheckButton.TextColor = Color.FromArgb("#C62828"); // Dark red
        }
        else if (verifiedCount > unverifiedCount)
        {
            FactCheckButton.BackgroundColor = Color.FromArgb("#E8F5E8"); // Light green
            FactCheckButton.TextColor = Color.FromArgb("#2E7D32"); // Dark green
        }
        else
        {
            // Default blue color
            FactCheckButton.BackgroundColor = Color.FromArgb("#E3F2FD");
            FactCheckButton.TextColor = Color.FromArgb("#1976D2");
        }
    }

    private async void OnShowFactCheckClicked(object? sender, EventArgs e)
    {
        if (_currentFactCheck == null)
        {
            await DisplayAlert("Fact Check", "No fact-check information available.", "OK");
            return;
        }

        var factCheckSummary = BuildFactCheckSummary(_currentFactCheck);
        await DisplayAlert("Story Fact Check", factCheckSummary, "OK");
    }

    private static string BuildFactCheckSummary(FactCheckSummary factCheck)
    {
        var summary = new System.Text.StringBuilder();
        
        // Overall assessment
        summary.AppendLine($"📊 OVERALL ASSESSMENT");
        summary.AppendLine(factCheck.OverallAssessment);
        summary.AppendLine();

        // Verified facts
        if (factCheck.VerifiedFacts.Count > 0)
        {
            summary.AppendLine($"✅ VERIFIED FACTS ({factCheck.VerifiedFacts.Count}):");
            foreach (var fact in factCheck.VerifiedFacts.Take(3)) // Show top 3
            {
                summary.AppendLine($"• {fact.Claim}");
                if (!string.IsNullOrWhiteSpace(fact.Evidence))
                {
                    summary.AppendLine($"  Evidence: {fact.Evidence}");
                }
            }
            if (factCheck.VerifiedFacts.Count > 3)
            {
                summary.AppendLine($"  ... and {factCheck.VerifiedFacts.Count - 3} more verified facts");
            }
            summary.AppendLine();
        }

        // Unverified claims
        if (factCheck.UnverifiedClaims.Count > 0)
        {
            summary.AppendLine($"❓ UNVERIFIED CLAIMS ({factCheck.UnverifiedClaims.Count}):");
            foreach (var claim in factCheck.UnverifiedClaims.Take(3)) // Show top 3
            {
                summary.AppendLine($"• {claim.Claim}");
                if (!string.IsNullOrWhiteSpace(claim.Evidence))
                {
                    summary.AppendLine($"  Note: {claim.Evidence}");
                }
            }
            if (factCheck.UnverifiedClaims.Count > 3)
            {
                summary.AppendLine($"  ... and {factCheck.UnverifiedClaims.Count - 3} more unverified claims");
            }
            summary.AppendLine();
        }

        // Contextual information
        if (factCheck.ContextualInfo.Count > 0)
        {
            summary.AppendLine($"ℹ️ CONTEXTUAL NOTES ({factCheck.ContextualInfo.Count}):");
            foreach (var info in factCheck.ContextualInfo.Take(2)) // Show top 2
            {
                summary.AppendLine($"• {info.Claim}");
            }
            summary.AppendLine();
        }

        // Warning for major inaccuracies
        if (factCheck.HasMajorInaccuracies)
        {
            summary.AppendLine("⚠️ This story contains potentially inaccurate information. Please verify important details independently.");
        }

        return summary.ToString();
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
            var storyText = string.IsNullOrWhiteSpace(StoryLabel.Text) ? null : StoryLabel.Text;

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
            UpdateRawResponse(GetRawResponse(ex));
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

    private void UpdateRawResponse(string? rawResponse)
    {
        _lastRawOpenAiResponse = string.IsNullOrWhiteSpace(rawResponse) ? null : rawResponse;
        _ = MainThread.InvokeOnMainThreadAsync(() =>
        {
            ShowRawResponseButton.IsVisible = _lastRawOpenAiResponse is not null;
        });
    }

    private static string? GetRawResponse(Exception? exception)
    {
        var current = exception;
        while (current is not null)
        {
            if (current.Data is IDictionary data && data.Contains(AiStoryService.RawResponseDataKey))
            {
                var raw = data[AiStoryService.RawResponseDataKey];
                if (raw is string text && !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }

                if (raw is not null)
                {
                    var textValue = raw.ToString();
                    if (!string.IsNullOrWhiteSpace(textValue))
                    {
                        return textValue;
                    }
                }
            }

            current = current.InnerException;
        }

        return null;
    }

    private async void OnShowRawResponseClicked(object? sender, EventArgs e)
    {
        var raw = _lastRawOpenAiResponse;
        if (string.IsNullOrWhiteSpace(raw))
        {
            ShowRawResponseButton.IsVisible = false;
            return;
        }

        var truncated = raw.Length > 4000 ? raw.Substring(0, 4000) + "…" : raw;
        var copied = false;

        try
        {
            await Clipboard.Default.SetTextAsync(raw);
            copied = true;
        }
        catch
        {
            // Ignore clipboard failures and fall back to showing the text.
        }

        if (copied)
        {
            await DisplayAlert(
                "Raw OpenAI response",
                $"The full response was copied to your clipboard so you can share it when reporting the parsing issue.\n\nPreview:\n{truncated}",
                "OK");
        }
        else
        {
            await DisplayAlert("Raw OpenAI response", truncated, "OK");
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

    private async void OnListenClicked(object? sender, EventArgs e)
    {
        var text = StoryLabel.Text;
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
        var hasText = !string.IsNullOrWhiteSpace(StoryLabel.Text);
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

    private sealed class ModelOption
    {
        public ModelOption(string label, string modelId, string? description = null)
        {
            Label = label;
            ModelId = modelId;
            Description = description;
        }

        public string Label { get; }
        public string ModelId { get; }
        public string? Description { get; }

        public string DisplayName => string.IsNullOrWhiteSpace(Description)
            ? Label
            : $"{Label} — {Description}";
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
