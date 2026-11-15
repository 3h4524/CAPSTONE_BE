using System.Text.RegularExpressions;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.BatchMockups.Printify;

/// <summary>
/// The garment colors of each Printify blueprint (<c>printify_color_map</c>): the color a mock-up recolors its white
/// photo with, named as Printify names it. Every template of the blueprint uses them.
/// </summary>
public interface IPrintifyColorMapService
{
    /// <summary>The blueprint's own colors, then the shared ones it has no own color of that name for.</summary>
    Task<Result<IReadOnlyList<PrintifyColorRowDto>>> ListAsync(long blueprintId, CancellationToken cancellationToken = default);

    /// <summary>Adds the blueprint's color of that name, or changes its hex.</summary>
    Task<Result<PrintifyColorRowDto>> SaveAsync(SavePrintifyColorRequestDto request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed partial class PrintifyColorMapService(
    IRepository<PrintifyColorMap> colors,
    PrintifyAdminCatalog catalog,
    IPrintifyAdminToken adminToken,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IPrintifyColorMapService
{
    public async Task<Result<IReadOnlyList<PrintifyColorRowDto>>> ListAsync(long blueprintId, CancellationToken cancellationToken = default)
    {
        var rows = await colors.Query()
            .Where(row => row.BlueprintId == null || row.BlueprintId == blueprintId)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.PrintifyName)
            .ToListAsync(cancellationToken);
        var own = rows.Where(row => row.BlueprintId == blueprintId).ToList();
        var named = own.Select(row => row.PrintifyName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Result.Success<IReadOnlyList<PrintifyColorRowDto>>(own
            .Concat(rows.Where(row => row.BlueprintId is null && !named.Contains(row.PrintifyName)))
            .Select(ToDto)
            .ToList());
    }

    public async Task<Result<PrintifyColorRowDto>> SaveAsync(SavePrintifyColorRequestDto request, CancellationToken cancellationToken = default)
    {
        if (adminToken.Token is not { } token)
            return Result.Failure<PrintifyColorRowDto>(PrintifyTemplateErrors.NoToken());
        // The name must be one Printify uses for this blueprint, or the color could never be matched to a variant.
        IReadOnlyDictionary<long, string> made;
        try
        {
            made = await catalog.ColorsOfAllProvidersAsync(token, request.BlueprintId, cancellationToken);
        }
        catch (PrintifyApiException exception)
        {
            return Result.Failure<PrintifyColorRowDto>(PrintifyAdminCatalog.Failure(exception));
        }
        var name = made.Values.FirstOrDefault(color => string.Equals(color, request.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return Result.Failure<PrintifyColorRowDto>(PrintifyTemplateErrors.ColorNotMade(request.Name ?? string.Empty));

        var saved = await UpsertAsync(request.BlueprintId, name, request.Hex, cancellationToken);
        if (saved.IsFailure)
            return Result.Failure<PrintifyColorRowDto>(saved.Error);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(saved.Value));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await colors.GetByIdAsync(id, cancellationToken);
        // A shared row (no blueprint) belongs to every blueprint: it is not removed from one.
        if (row is null || row.BlueprintId is null)
            return Result.Failure(PrintifyTemplateErrors.ColorRowNotFound());
        await colors.RemoveAsync(row, saveChange: true, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Adds or changes the blueprint's own color of that name without saving: the caller saves it, alone or with a
    /// template made in the same transaction.
    /// </summary>
    internal async Task<Result<PrintifyColorMap>> UpsertAsync(long blueprintId, string name, string? hex, CancellationToken cancellationToken)
    {
        var normalized = hex?.Trim().ToUpperInvariant();
        if (normalized is null || !HexPattern().IsMatch(normalized))
            return Result.Failure<PrintifyColorMap>(PrintifyTemplateErrors.InvalidHex());

        var own = await colors.Query()
            .Where(row => row.BlueprintId == blueprintId)
            .ToListAsync(cancellationToken);
        var row = own.FirstOrDefault(item => string.Equals(item.PrintifyName, name, StringComparison.OrdinalIgnoreCase));
        if (own.Any(item => item != row && string.Equals(item.Hex.Trim(), normalized, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure<PrintifyColorMap>(PrintifyTemplateErrors.HexTaken(normalized));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (row is null)
        {
            row = new PrintifyColorMap { Id = Guid.NewGuid(), BlueprintId = blueprintId, PrintifyName = name, Hex = normalized, CreatedAt = now, UpdatedAt = now };
            await colors.AddAsync(row, saveChange: false, cancellationToken);
            return Result.Success(row);
        }
        row.Hex = normalized;
        row.UpdatedAt = now;
        return Result.Success(row);
    }

    private static PrintifyColorRowDto ToDto(PrintifyColorMap row) =>
        new(row.Id, row.PrintifyName, row.Hex.Trim().ToUpperInvariant(), row.BlueprintId, row.BlueprintId is null);

    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex HexPattern();
}
