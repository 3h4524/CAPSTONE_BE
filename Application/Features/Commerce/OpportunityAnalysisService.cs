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
    private const string PromptVersion = "opportunity-analyst-v1";
    private const string SchemaVersion = "opportunity-analysis-v1";

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
                x.Snapshot.MatchedQuery
            })
            .ToListAsync(cancellationToken);

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
                    $"Analyze this seller-specific market opportunity. Return ONLY valid JSON matching this schema: " +
                    """{"summary":"string","recommendation":"string","supportingPoints":["string"],"risks":["string"],"actionPlan":["string"],"confidenceNote":"string"}""" +
                    "\nDo not invent facts. Use only the supplied scores and evidence IDs. Mark uncertainty clearly.\nINPUT:\n" + inputSnapshot,
                    "You are an evidence-grounded Etsy opportunity analyst. Never copy listing text. Keep the answer concise and actionable."),
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
