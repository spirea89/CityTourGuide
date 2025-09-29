using System;
using System.Collections.Generic;

namespace CityTour.Models;

public sealed class StoryFactCheckResult
{
    public StoryFactCheckResult(
        string verdict,
        string summary,
        IReadOnlyList<string> confirmedFacts,
        IReadOnlyList<FactCheckWarning> warnings)
    {
        Verdict = string.IsNullOrWhiteSpace(verdict) ? "aligned" : verdict.Trim();
        Summary = summary?.Trim() ?? string.Empty;
        ConfirmedFacts = confirmedFacts ?? Array.Empty<string>();
        Warnings = warnings ?? Array.Empty<FactCheckWarning>();
    }

    public string Verdict { get; }

    public string Summary { get; }

    public IReadOnlyList<string> ConfirmedFacts { get; }

    public IReadOnlyList<FactCheckWarning> Warnings { get; }
}

public sealed class FactCheckWarning
{
    public FactCheckWarning(string claim, string issue, string recommendation)
    {
        Claim = claim?.Trim() ?? string.Empty;
        Issue = issue?.Trim() ?? string.Empty;
        Recommendation = recommendation?.Trim() ?? string.Empty;
    }

    public string Claim { get; }

    public string Issue { get; }

    public string Recommendation { get; }
}
