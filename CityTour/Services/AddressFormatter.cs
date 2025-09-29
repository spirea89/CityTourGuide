using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace CityTour.Services
{
    public static class AddressFormatter
    {
        private static readonly Regex MultipleSpaces = new Regex("\\s{2,}", RegexOptions.Compiled);
        private static readonly Regex PostalCodePattern = new Regex("\\b\\d{3,5}\\b", RegexOptions.Compiled);

        public static ParsedAddress Parse(string? rawAddress)
        {
            if (string.IsNullOrWhiteSpace(rawAddress))
            {
                return new ParsedAddress(rawAddress, null, null, null);
            }

            var segments = rawAddress
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToList();

            if (segments.Count == 0)
            {
                return new ParsedAddress(rawAddress, null, null, null);
            }

            string? street = Normalize(segments[0]);
            if (string.IsNullOrWhiteSpace(street))
            {
                street = null;
            }

            string? country = null;
            var citySearchUpperBound = segments.Count - 1;

            if (segments.Count >= 3)
            {
                country = Normalize(segments[segments.Count - 1]);
                if (string.IsNullOrWhiteSpace(country))
                {
                    country = null;
                }
                else
                {
                    citySearchUpperBound = segments.Count - 2;
                }
            }

            string? city = null;
            for (var i = citySearchUpperBound; i >= 1; i--)
            {
                var candidate = Normalize(segments[i]);
                var cleaned = StripPostalCode(candidate);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    city = cleaned;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(city) && segments.Count >= 2)
            {
                var fallback = StripPostalCode(segments[1]);
                city = string.IsNullOrWhiteSpace(fallback) ? null : fallback;
            }

            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(country) &&
                string.Equals(city, country, StringComparison.OrdinalIgnoreCase))
            {
                country = null;
            }

            return new ParsedAddress(rawAddress, street, city, country);
        }

        public static string? GetDisplayAddress(string? rawAddress)
        {
            var parsed = Parse(rawAddress);
            return parsed.DisplayAddress ?? parsed.Original;
        }

        public static string? GetStoryAddress(string? rawAddress)
        {
            var parsed = Parse(rawAddress);
            return parsed.StoryAddress ?? parsed.Original;
        }

        private static string Normalize(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var trimmed = input.Trim();
            return MultipleSpaces.Replace(trimmed, " ");
        }

        private static string StripPostalCode(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var normalized = Normalize(input);
            var withoutPostal = PostalCodePattern.Replace(normalized, string.Empty);
            withoutPostal = MultipleSpaces.Replace(withoutPostal, " ");
            return withoutPostal.Trim(',', '-', ' ').Trim();
        }

        private static string? CombineParts(params string?[] parts)
        {
            var filtered = parts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim())
                .Where(part => part.Length > 0)
                .ToList();

            return filtered.Count == 0 ? null : string.Join(", ", filtered);
        }

        public sealed class ParsedAddress
        {
            public ParsedAddress(string? original, string? streetAndNumber, string? city, string? country)
            {
                Original = original;
                StreetAndNumber = streetAndNumber;
                City = city;
                Country = country;
            }

            public string? Original { get; }

            public string? StreetAndNumber { get; }

            public string? City { get; }

            public string? Country { get; }

            public string? DisplayAddress => CombineParts(StreetAndNumber, City, Country);

            public string? StoryAddress => CombineParts(StreetAndNumber, City);
        }
    }
}
