using System.Text.Json;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Commerce;

public sealed record OpportunityAnalysisResponse(
    Guid RunId,
    Guid SellerOpportunityId,
    string Status,
    string Provider,
    string Model,
    string Summary,
    string Recommendation,
    IReadOnlyList<string> SupportingPoints,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> ActionPlan,
    string ConfidenceNote);

public interface IOpportunityAnalysisService
{
    Task<OpportunityAnalysisResponse?> AnalyzeAsync(Guid sellerOpportunityId, CancellationToken cancellationToken = default);
    Task<OpportunityAnalysisResponse?> GetLatestAsync(Guid sellerOpportunityId, CancellationToken cancellationToken = default);
}

public sealed class OpportunityAnalysisService(
    ICurrentUser currentUser,
    IRepository<SellerOpportunity> sellerOpportunities,
    IRepository<MarketOpportunityEvidence> evidence,
    IRepository<OpportunityAnalysisRun> runs,
    IUnitOfWork unitOfWork,
    IGeminiTextGenerationProvider textGeneration) : IOpportunityAnalysisService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string PromptVersion = "opportunity-action-navigator-v2";
    private const string SchemaVersion = "opportunity-analysis-v2";

    public async Task<OpportunityAnalysisResponse?> AnalyzeAsync(
        Guid sellerOpportunityId,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException("Authentication is required.");
        var sellerOpportunity = await sellerOpportunities.Query()
            .Where(x => x.Id == sellerOpportunityId && x.UserId == userId)
            .Include(x => x.Opportunity)
            .SingleOrDefaultAsync(cancellationToken);
        if (sellerOpportunity is null) return null;

        var evidenceRows = await evidence.Query()
            .Where(x => x.OpportunityId == sellerOpportunity.OpportunityId)
            .OrderBy(x => x.EvidenceRank)
            .Take(10)
            .Select(x => new
            {
                x.SnapshotId,
                x.RagDocumentId,
                x.EvidenceRole,
                x.EvidenceRank,
                x.Snapshot.Title,
                x.Snapshot.SourceUrl,
                x.Snapshot.Price,
                x.Snapshot.Currency,
                x.Snapshot.Favorites,
                x.Snapshot.Views,
                x.Snapshot.MatchedQuery,
                x.Snapshot.ExternalListingId,
                x.Snapshot.ObservedAt,
                x.Snapshot.IsSynthetic
            })
            .ToListAsync(cancellationToken);

        var evidenceAll = await evidence.Query()
            .Where(x => x.OpportunityId == sellerOpportunity.OpportunityId)
            .Select(x => new
            {
                x.Snapshot.ExternalListingId,
                x.Snapshot.ObservedAt,
                x.Snapshot.IsSynthetic
            })
            .ToListAsync(cancellationToken);
        var evidenceSummary = new
        {
            totalEvidence = evidenceAll.Count,
            realEvidence = evidenceAll.Count(x => !x.IsSynthetic),
            syntheticEvidence = evidenceAll.Count(x => x.IsSynthetic),
            distinctListings = evidenceAll.Select(x => x.ExternalListingId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            observationPeriods = evidenceAll.Select(x => x.ObservedAt.Date).Distinct().Count()
        };

        var input = new
        {
            sellerOpportunityId,
            sellerOpportunity.Opportunity.Platform,
            sellerOpportunity.Opportunity.Title,
            sellerOpportunity.Opportunity.Niche,
            sellerOpportunity.Opportunity.Category,
            sellerOpportunity.Opportunity.Summary,
            marketScore = sellerOpportunity.Opportunity.MarketOpportunityScore,
            confidence = sellerOpportunity.Opportunity.Confidence,
            sellerFitScore = sellerOpportunity.SellerFitScore,
            finalScore = sellerOpportunity.FinalScore,
            fitBreakdown = sellerOpportunity.FitBreakdown,
            evidenceSummary,
            evidence = evidenceRows
        };
        var inputSnapshot = JsonSerializer.Serialize(input, JsonOptions);
        var run = new OpportunityAnalysisRun
        {
            Id = Guid.NewGuid(),
            SellerOpportunityId = sellerOpportunityId,
            Status = "running",
            Provider = "gemini",
            PromptVersion = PromptVersion,
            SchemaVersion = SchemaVersion,
            InputSnapshot = inputSnapshot,
            RequestedAt = DbNow(),
            StartedAt = DbNow()
        };
        await runs.AddAsync(run, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var generated = await textGeneration.GenerateAsync(
                new GeminiTextGenerationRequest(
                    $"Act as an action navigator for an Etsy seller. Analyze this seller-specific market opportunity and return ONLY valid JSON matching this schema: " +
                    """{"summary":"string","recommendation":"string","supportingPoints":["string"],"risks":["string"],"actionPlan":["string"],"confidenceNote":"string"}""" +
                    "\nRules:\n" +
                    "- Start with the decision the seller should make now: validate, test, hold, or avoid.\n" +
                    "- Treat market scores as proxies, not sales. Never claim demand, revenue, or bestseller status unless supplied.\n" +
                    "- If syntheticEvidence is greater than zero, explicitly state that the signal is simulated. If realEvidence is zero, recommendation must be validation-first and must not recommend scaling.\n" +
                    "- Use the seller fit breakdown to explain what the seller should change: category, keywords, price, or positioning.\n" +
                    "- supportingPoints must cite concrete supplied numbers (scores, counts, price, trend, or evidence summary).\n" +
                    "- risks must be specific and decision-relevant, not generic warnings.\n" +
                    "- Return exactly 3 actionPlan items: (1) an immediate action within 24 hours, (2) a validation action within 7 days, and (3) a go/no-go gate with a measurable threshold. Each item must state Do -> Why -> Success check.\n" +
                    "- Do not repeat vague advice such as 'refine keywords'. Name the exact keyword/category/price decision using only the input.\n" +
                    "- Keep every field concise. Never copy listing text or invent missing Etsy transaction data.\nINPUT:\n" + inputSnapshot,
                    "You are an evidence-grounded Etsy action navigator. Convert supplied evidence into a concrete next decision while clearly separating synthetic signals from real market evidence."),
                cancellationToken);
            var parsed = Parse(generated.Text);

            run.Status = "succeeded";
            run.Model = generated.Model;
            run.OutputSnapshot = generated.Text;
            run.CompletedAt = DbNow();
            await runs.UpdateAsync(run, cancellationToken: cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new(run.Id, sellerOpportunityId, run.Status, run.Provider!, run.Model, parsed.Summary,
                parsed.Recommendation, parsed.SupportingPoints, parsed.Risks, parsed.ActionPlan, parsed.ConfidenceNote);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Status = "failed";
            run.ErrorMessage = exception.Message;
            run.CompletedAt = DbNow();
            await runs.UpdateAsync(run, cancellationToken: cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OpportunityAnalysisResponse?> GetLatestAsync(
        Guid sellerOpportunityId,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException("Authentication is required.");
        var run = await runs.Query()
            .Where(x => x.SellerOpportunityId == sellerOpportunityId
                && x.SellerOpportunity.UserId == userId
                && x.Status == "succeeded"
                && x.OutputSnapshot != null)
            .OrderByDescending(x => x.CompletedAt ?? x.RequestedAt)
            .Select(x => new
            {
                x.Id,
                x.SellerOpportunityId,
                x.Status,
                x.Provider,
                x.Model,
                x.OutputSnapshot
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null) return null;

        var parsed = Parse(run.OutputSnapshot!);
        return new(
            run.Id,
            run.SellerOpportunityId,
            run.Status,
            run.Provider ?? "unknown",
            run.Model ?? "unknown",
            parsed.Summary,
            parsed.Recommendation,
            parsed.SupportingPoints,
            parsed.Risks,
            parsed.ActionPlan,
            parsed.ConfidenceNote);
    }

    private static ParsedAnalysis Parse(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        return new(
            Required(root, "summary"),
            Required(root, "recommendation"),
            ReadArray(root, "supportingPoints"),
            ReadArray(root, "risks"),
            ReadArray(root, "actionPlan"),
            Required(root, "confidenceNote"));
    }

    private static string Required(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException($"Gemini response is missing required field '{property}'.");

    private static IReadOnlyList<string> ReadArray(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x)).Take(8).ToArray()
            : [];

    private sealed record ParsedAnalysis(
        string Summary,
        string Recommendation,
        IReadOnlyList<string> SupportingPoints,
        IReadOnlyList<string> Risks,
        IReadOnlyList<string> ActionPlan,
        string ConfidenceNote);

    private static DateTime DbNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}
