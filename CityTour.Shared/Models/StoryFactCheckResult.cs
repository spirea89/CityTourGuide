namespace CityTour.Models;

public sealed record StoryFactCheckResult(
    string Story,
    string Prompt,
    FactCheckSummary FactCheck);

public sealed record FactCheckSummary(
    List<FactCheckItem> VerifiedFacts,
    List<FactCheckItem> UnverifiedClaims,
    List<FactCheckItem> ContextualInfo,
    bool HasMajorInaccuracies,
    string OverallAssessment);

public sealed record FactCheckItem(
    string Claim,
    FactCheckStatus Status,
    string? Evidence,
    string? Source);

public enum FactCheckStatus
{
    Verified,
    Unverified,
    Contextual,
    Inaccurate,
    Uncertain
}