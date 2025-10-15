using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CityTour.Services
{
    public interface IWikipediaService
    {
    Task<WikipediaSummary?> FetchSummaryAsync(
        string buildingName,
        string? address,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default);
}

    public sealed class WikipediaSummary
    {
        public WikipediaSummary(
            string title,
            string normalizedTitle,
            string language,
            string url,
            string? extract,
            string? description,
            string? lastModified)
        {
            Title = title;
            NormalizedTitle = normalizedTitle;
            Language = language;
            Url = url;
            Extract = extract;
            Description = description;
            LastModified = lastModified;
        }

        public string Title { get; }

        public string NormalizedTitle { get; }

        public string Language { get; }

        public string Url { get; }

        public string? Extract { get; }

        public string? Description { get; }

        public string? LastModified { get; }
    }

    public class WikipediaService : IWikipediaService
    {
    private const string UserAgent = "CityTourGuide/1.0";

    private readonly HttpClient _httpClient;
    private readonly IApiKeyProvider _apiKeys;

    public WikipediaService(HttpClient httpClient, IApiKeyProvider apiKeys)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKeys = apiKeys ?? throw new ArgumentNullException(nameof(apiKeys));
    }

    public async Task<WikipediaSummary?> FetchSummaryAsync(
        string buildingName,
        string? address,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildingName) && string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("A building name or address is required.", nameof(buildingName));
        }

        var languages = BuildLanguageCandidates();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();
        PopulateTitleCandidates(candidates, seen, buildingName, address);

        if (latitude.HasValue && longitude.HasValue)
        {
            var geoTitles = await FetchNearbyTitlesAsync(latitude.Value, longitude.Value, languages, cancellationToken);
            foreach (var title in geoTitles)
            {
                AddCandidate(candidates, seen, title);
            }
        }

        foreach (var lang in languages)
        {
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var summary = await TryFetchSummaryAsync(candidate, lang, cancellationToken);
                if (summary is not null)
                {
                    return summary;
                }
            }
        }

        return null;
    }

    private static void PopulateTitleCandidates(
        ICollection<string> candidates,
        HashSet<string> seen,
        string buildingName,
        string? address)
    {
        AddCandidate(candidates, seen, buildingName);

        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        AddCandidate(candidates, seen, address);

        var parsed = AddressFormatter.Parse(address);
        AddCandidate(candidates, seen, parsed.DisplayAddress);
        AddCandidate(candidates, seen, parsed.StoryAddress);
        AddCandidate(candidates, seen, parsed.City);
        AddCandidate(candidates, seen, parsed.Country);

        var baseName = string.IsNullOrWhiteSpace(buildingName)
            ? parsed.DisplayAddress ?? parsed.Original
            : buildingName;

        if (!string.IsNullOrWhiteSpace(baseName))
        {
            foreach (var segment in new[] { parsed.City, parsed.Country })
            {
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                AddCandidate(candidates, seen, $"{baseName}, {segment}");
                AddCandidate(candidates, seen, $"{baseName} ({segment})");
                AddCandidate(candidates, seen, $"{baseName} {segment}");
            }
        }
    }

    private static void AddCandidate(ICollection<string> candidates, HashSet<string> seen, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (seen.Add(trimmed))
        {
            candidates.Add(trimmed);
        }
    }

    private static List<string> BuildLanguageCandidates()
    {
        var result = new List<string>();
        var culture = CultureInfo.CurrentUICulture ?? CultureInfo.CurrentCulture;
        var primary = NormalizeLanguage(culture?.TwoLetterISOLanguageName);
        result.Add(primary);

        if (!string.Equals(primary, "en", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("en");
        }

        return result;
    }

    private async Task<WikipediaSummary?> TryFetchSummaryAsync(string title, string language, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var normalizedLang = NormalizeLanguage(language);
        var encodedTitle = NormalizeTitleForUrl(title);
        if (string.IsNullOrWhiteSpace(encodedTitle))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_apiKeys.WikipediaApiKey))
        {
            var primaryUrl = $"https://api.wikimedia.org/core/v1/wikipedia/{normalizedLang}/page/summary/{encodedTitle}";
            var primaryResult = await RequestSummaryAsync(primaryUrl, normalizedLang, title, includeApiKey: true, cancellationToken);
            if (primaryResult.IsSuccess)
            {
                return primaryResult.Summary;
            }

            if (primaryResult.Fatal)
            {
                throw new InvalidOperationException(primaryResult.ErrorMessage ?? "Wikipedia integration failed.");
            }
        }

        var fallbackUrl = $"https://{normalizedLang}.wikipedia.org/api/rest_v1/page/summary/{encodedTitle}";
        var fallbackResult = await RequestSummaryAsync(fallbackUrl, normalizedLang, title, includeApiKey: false, cancellationToken);
        if (fallbackResult.IsSuccess)
        {
            return fallbackResult.Summary;
        }

        if (fallbackResult.Fatal)
        {
            throw new InvalidOperationException(fallbackResult.ErrorMessage ?? "Wikipedia integration failed.");
        }

        return null;
    }

    private async Task<RequestResult> RequestSummaryAsync(
        string url,
        string language,
        string fallbackTitle,
        bool includeApiKey,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(url, language, includeApiKey);
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RequestResult.Failure($"Could not reach Wikipedia: {ex.Message}", fatal: true);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return RequestResult.NotFound();
            }

            if (!response.IsSuccessStatusCode)
            {
                string? body = null;
                try
                {
                    body = await response.Content.ReadAsStringAsync();
                }
                catch
                {
                    // ignore
                }

                var detail = TryExtractErrorMessage(body);
                var message = BuildErrorMessage(response.StatusCode, detail, includeApiKey);
                var fatal = true;

                if (includeApiKey && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden))
                {
                    fatal = false;
                }

                return RequestResult.Failure(message, fatal);
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var summary = ParseSummary(document.RootElement, language, fallbackTitle);
                return summary is not null ? RequestResult.Success(summary) : RequestResult.NotFound();
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return RequestResult.Failure($"Wikipedia returned an unreadable response: {ex.Message}", fatal: true);
            }
        }
    }

    private async Task<IReadOnlyList<string>> FetchNearbyTitlesAsync(
        double latitude,
        double longitude,
        IReadOnlyList<string> languages,
        CancellationToken cancellationToken)
    {
        var results = new List<string>();
        var latText = latitude.ToString(CultureInfo.InvariantCulture);
        var lonText = longitude.ToString(CultureInfo.InvariantCulture);

        foreach (var language in languages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var safeLang = NormalizeLanguage(language);
            var url =
                $"https://{safeLang}.wikipedia.org/w/api.php?action=query&list=geosearch&gscoord={latText}%7C{lonText}&gsradius=200&gslimit=5&format=json";

            using var request = CreateRequest(url, safeLang, includeApiKey: false);
            HttpResponseMessage response;

            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                continue;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                try
                {
                    await using var stream = await response.Content.ReadAsStreamAsync();
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    if (document.RootElement.TryGetProperty("query", out var query) &&
                        query.TryGetProperty("geosearch", out var array) &&
                        array.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in array.EnumerateArray())
                        {
                            if (entry.TryGetProperty("title", out var titleElement) &&
                                titleElement.ValueKind == JsonValueKind.String)
                            {
                                var title = titleElement.GetString();
                                if (!string.IsNullOrWhiteSpace(title))
                                {
                                    results.Add(title);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // ignore parsing errors for geosearch
                }
            }
        }

        return results;
    }

    private HttpRequestMessage CreateRequest(string url, string language, bool includeApiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.ParseAdd(UserAgent);

        if (!string.IsNullOrWhiteSpace(language))
        {
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        }

        if (includeApiKey)
        {
            var key = _apiKeys.WikipediaApiKey;
            if (!string.IsNullOrWhiteSpace(key))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }
        }

        return request;
    }

    private static WikipediaSummary? ParseSummary(JsonElement root, string fallbackLang, string fallbackTitle)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var lang = NormalizeLanguage(ExtractString(root, "lang") ?? fallbackLang);

        var normalizedTitle = ExtractFromObject(root, "titles", "normalized")
            ?? ExtractFromObject(root, "titles", "canonical")
            ?? fallbackTitle;

        var displayTitle = ExtractString(root, "displaytitle")
            ?? ExtractString(root, "title")
            ?? normalizedTitle
            ?? fallbackTitle;

        var extract = ExtractString(root, "extract");
        var description = ExtractString(root, "description");
        var lastModified = ExtractString(root, "timestamp") ?? ExtractString(root, "last_modified");

        var url = TryExtractPageUrl(root)
            ?? BuildFallbackPageUrl(lang, normalizedTitle ?? displayTitle ?? fallbackTitle);

        return new WikipediaSummary(
            displayTitle ?? fallbackTitle,
            normalizedTitle ?? displayTitle ?? fallbackTitle,
            lang,
            url,
            string.IsNullOrWhiteSpace(extract) ? null : extract,
            string.IsNullOrWhiteSpace(description) ? null : description,
            string.IsNullOrWhiteSpace(lastModified) ? null : lastModified);
    }

    private static string? ExtractString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? ExtractFromObject(JsonElement element, string objectProperty, string innerProperty)
    {
        if (!element.TryGetProperty(objectProperty, out var obj) || obj.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ExtractString(obj, innerProperty);
    }

    private static string? TryExtractPageUrl(JsonElement root)
    {
        if (root.TryGetProperty("content_urls", out var contentUrls) && contentUrls.ValueKind == JsonValueKind.Object)
        {
            if (contentUrls.TryGetProperty("desktop", out var desktop) && desktop.ValueKind == JsonValueKind.Object)
            {
                var page = ExtractString(desktop, "page");
                if (!string.IsNullOrWhiteSpace(page))
                {
                    return page;
                }
            }

            if (contentUrls.TryGetProperty("mobile", out var mobile) && mobile.ValueKind == JsonValueKind.Object)
            {
                var page = ExtractString(mobile, "page");
                if (!string.IsNullOrWhiteSpace(page))
                {
                    return page;
                }
            }
        }

        var canonical = ExtractString(root, "canonicalurl");
        if (!string.IsNullOrWhiteSpace(canonical))
        {
            return canonical;
        }

        return null;
    }

    private static string BuildFallbackPageUrl(string language, string? title)
    {
        var safeLang = NormalizeLanguage(language);
        if (string.IsNullOrWhiteSpace(title))
        {
            return $"https://{safeLang}.wikipedia.org/";
        }

        var safeTitle = NormalizeTitleForUrl(title);
        return $"https://{safeLang}.wikipedia.org/wiki/{safeTitle}";
    }

    private static string NormalizeTitleForUrl(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var trimmed = title.Trim().Replace(' ', '_');
        return Uri.EscapeDataString(trimmed);
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "en";
        }

        var trimmed = language.Trim().ToLowerInvariant();
        var builder = new StringBuilder(trimmed.Length);

        foreach (var ch in trimmed)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_')
            {
                builder.Append(ch);
            }
        }

        return builder.Length == 0 ? "en" : builder.ToString();
    }

    private static string? TryExtractErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var detail = ExtractString(root, "detail") ?? ExtractString(root, "message");
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return detail;
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var info = ExtractString(error, "info");
                if (!string.IsNullOrWhiteSpace(info))
                {
                    return info;
                }
            }
        }
        catch (JsonException)
        {
            // ignore parse errors
        }

        return null;
    }

    private static string BuildErrorMessage(HttpStatusCode statusCode, string? detail, bool includeApiKey)
    {
        var builder = new StringBuilder();
        builder.Append("Wikipedia request failed (")
            .Append((int)statusCode)
            .Append(' ')
            .Append(statusCode)
            .Append(')');

        if (!string.IsNullOrWhiteSpace(detail))
        {
            builder.Append(':').Append(' ').Append(detail.Trim());
        }

        if (includeApiKey && (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden))
        {
            builder.Append(". Check your Wikimedia API key configuration.");
        }

        return builder.ToString();
    }

    private readonly struct RequestResult
    {
        public RequestResult(WikipediaSummary? summary, bool isSuccess, bool fatal, string? errorMessage)
        {
            Summary = summary;
            IsSuccess = isSuccess;
            Fatal = fatal;
            ErrorMessage = errorMessage;
        }

        public WikipediaSummary? Summary { get; }

        public bool IsSuccess { get; }

        public bool Fatal { get; }

        public string? ErrorMessage { get; }

        public static RequestResult Success(WikipediaSummary summary)
        {
            return new RequestResult(summary, true, false, null);
        }

        public static RequestResult NotFound()
        {
            return new RequestResult(null, false, false, null);
        }

        public static RequestResult Failure(string? message, bool fatal)
        {
            return new RequestResult(null, false, fatal, message);
        }
    }
}
}
