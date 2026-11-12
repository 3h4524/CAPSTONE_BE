using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Admin.MockupTemplates;

public interface IAdminMockupTemplateService
{
    /// <summary>Every system mock-up template, with the Printify product it is linked to.</summary>
    Task<Result<IReadOnlyList<AdminMockupTemplateDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Links a system template to the Printify product its photo shows, or changes the link.</summary>
    Task<Result<AdminMockupTemplateDto>> LinkPrintifyAsync(Guid templateId, LinkMockupTemplatePrintifyRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Removes a system template's Printify link; workflows pushing to Printify stop offering it.</summary>
    Task<Result> UnlinkPrintifyAsync(Guid templateId, CancellationToken cancellationToken = default);
}

/// <summary>A system mock-up template as an Admin manages it.</summary>
public sealed record AdminMockupTemplateDto(
    Guid Id,
    string Name,
    string ProductType,
    string Kind,
    string BaseImageUrl,
    string? PreviewImageUrl,
    bool IsActive,
    MockupTemplatePrintifyLinkDto? Printify);

/// <summary>The Printify product a template's photo shows, and the provider and sizes offered first.</summary>
public sealed record MockupTemplatePrintifyLinkDto(
    long BlueprintId,
    string BlueprintTitle,
    string? BlueprintImageUrl,
    long? PrintProviderId,
    string? PrintProviderTitle,
    IReadOnlyList<string> DefaultSizes)
{
    public static MockupTemplatePrintifyLinkDto From(TemplatePrintifyLink link) =>
        new(link.BlueprintId, link.BlueprintTitle, link.BlueprintImageUrl, link.PrintProviderId, link.PrintProviderTitle, link.DefaultSizes);
}

/// <param name="BlueprintImageUrl">Printify's photo of the blank product, kept to compare with the template.</param>
public sealed record LinkMockupTemplatePrintifyRequestDto(
    long BlueprintId,
    string BlueprintTitle,
    string? BlueprintImageUrl,
    long? PrintProviderId,
    string? PrintProviderTitle,
    IReadOnlyList<string>? DefaultSizes);

/// <summary>
/// Lets an Admin link each system mock-up template to the Printify product its photo shows. A workflow that pushes to
/// Printify offers only linked templates and makes the linked product, so a listing shows the garment that ships.
/// </summary>
public sealed class AdminMockupTemplateService(
    ICurrentUser currentUser,
    IRepository<MockupTemplate> templates,
    IMockupTemplatePrintifyLinkRepository links) : IAdminMockupTemplateService
{
    public const int MaximumTitleLength = 200;
    public const int MaximumSizes = 50;

    public async Task<Result<IReadOnlyList<AdminMockupTemplateDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var system = await templates.Query()
            .Where(template => template.IsSystemTemplate == true)
            .OrderBy(template => template.ProductType).ThenBy(template => template.Kind).ThenBy(template => template.Name)
            .ToListAsync(cancellationToken);
        var linked = await links.GetAsync(system.Select(template => template.Id).ToArray(), cancellationToken);
        return Result.Success<IReadOnlyList<AdminMockupTemplateDto>>(system.Select(template => Map(template, linked.GetValueOrDefault(template.Id))).ToList());
    }

    public async Task<Result<AdminMockupTemplateDto>> LinkPrintifyAsync(Guid templateId, LinkMockupTemplatePrintifyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid adminId)
            return Result.Failure<AdminMockupTemplateDto>(Error.Unauthorized("Auth.Unauthenticated", "Sign in again to link templates."));
        if (Problem(request) is { } problem)
            return Result.Failure<AdminMockupTemplateDto>(Error.Validation(problem));
        var template = await templates.Query().SingleOrDefaultAsync(item => item.Id == templateId, cancellationToken);
        if (template is not { IsSystemTemplate: true })
            return Result.Failure<AdminMockupTemplateDto>(NotFound());

        var link = new TemplatePrintifyLink(template.Id, request.BlueprintId, request.BlueprintTitle.Trim(),
            string.IsNullOrWhiteSpace(request.BlueprintImageUrl) ? null : request.BlueprintImageUrl.Trim(),
            request.PrintProviderId, string.IsNullOrWhiteSpace(request.PrintProviderTitle) ? null : request.PrintProviderTitle.Trim(),
            (request.DefaultSizes ?? []).Select(size => size.Trim()).Where(size => size.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        await links.SaveAsync(link, adminId, cancellationToken);
        return Result.Success(Map(template, link));
    }

    public async Task<Result> UnlinkPrintifyAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        var template = await templates.Query().SingleOrDefaultAsync(item => item.Id == templateId, cancellationToken);
        if (template is not { IsSystemTemplate: true })
            return Result.Failure(NotFound());
        await links.DeleteAsync(templateId, cancellationToken);
        return Result.Success();
    }

    private static string? Problem(LinkMockupTemplatePrintifyRequestDto request) =>
        request.BlueprintId <= 0 ? "Choose the Printify product this template shows."
        : string.IsNullOrWhiteSpace(request.BlueprintTitle) || request.BlueprintTitle.Length > MaximumTitleLength ? "The Printify product needs a name of at most 200 characters."
        : request.PrintProviderId is <= 0 ? "Choose a valid print provider, or none."
        : request.DefaultSizes is { Count: > MaximumSizes } ? $"Choose at most {MaximumSizes} sizes."
        : request.BlueprintImageUrl is { } url && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            ? "The Printify product photo must be an https address."
        : null;

    private static Error NotFound() => Error.NotFound("MockupTemplate.NotFound", "System mock-up template not found.");

    private static AdminMockupTemplateDto Map(MockupTemplate template, TemplatePrintifyLink? link) =>
        new(template.Id, template.Name, template.ProductType, template.Kind, template.BaseImageUrl, template.PreviewImageUrl,
            template.IsActive == true, link is null ? null : MockupTemplatePrintifyLinkDto.From(link));
}
