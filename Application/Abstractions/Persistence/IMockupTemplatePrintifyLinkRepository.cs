namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// The Printify product a system mock-up template's photo shows, so a listing made through Printify shows the garment
/// Printify ships. Stored in <c>mockup_template_printify_links</c>.
/// </summary>
public interface IMockupTemplatePrintifyLinkRepository
{
    /// <summary>
    /// The links of the given templates, by template id; templates without one are left out. Empty, not an error, while
    /// the table does not exist yet on the database (its migration not applied), so the template lists keep working.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, TemplatePrintifyLink>> GetAsync(IReadOnlyCollection<Guid> templateIds, CancellationToken cancellationToken);

    /// <summary>Links the template, or replaces its link.</summary>
    Task SaveAsync(TemplatePrintifyLink link, Guid updatedBy, CancellationToken cancellationToken);

    /// <summary>Removes the template's link; true when it had one.</summary>
    Task<bool> DeleteAsync(Guid templateId, CancellationToken cancellationToken);
}

/// <summary>A template's Printify product, with the provider and sizes offered first.</summary>
/// <param name="BlueprintImageUrl">Printify's photo of the blank product, shown next to the template to compare.</param>
public sealed record TemplatePrintifyLink(
    Guid TemplateId,
    long BlueprintId,
    string BlueprintTitle,
    string? BlueprintImageUrl,
    long? PrintProviderId,
    string? PrintProviderTitle,
    IReadOnlyList<string> DefaultSizes);
