using System.Security.Cryptography;
using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Batches.Validators;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.BatchMockups;

/// <summary>Makes a model photo with AI when the library has none of the people wanted.</summary>
public interface IModelPhotoGenerationService
{
    /// <summary>
    /// Has Ideogram make a photo of the people described in a plain white t-shirt, with the caller's Ideogram key, and
    /// saves it as a model photo: the seller's own, or the shared library's when <paramref name="system"/>.
    /// </summary>
    Task<Result<MockupTemplateResponseDto>> GenerateAsync(GenerateModelPhotoRequestDto request, bool system, CancellationToken cancellationToken = default);
}

/// <remarks>
/// The photo is made once and kept: mock-ups are then put on it without AI, so the print is exactly the design.
/// The prompt is fixed wording for the attributes chosen (<see cref="ModelPhotoPrompts"/>), never free text. The
/// garment is found automatically, as for an uploaded photo, and designs are placed by measuring it.
/// </remarks>
public sealed class ModelPhotoGenerationService(
    ICurrentUser currentUser,
    IModelPhotoService modelPhotos,
    IRepository<MockupTemplate> templates,
    IRepository<ApiUsageRecord> usage,
    IApiKeyRepository keys,
    IApiKeyCredentials credentials,
    IEnumerable<IImageGenerationProvider> imageProviders,
    IMockupMapService maps,
    TimeProvider time,
    ILogger<ModelPhotoGenerationService> logger) : IModelPhotoGenerationService
{
    public async Task<Result<MockupTemplateResponseDto>> GenerateAsync(GenerateModelPhotoRequestDto request, bool system, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.Unauthenticated("create"));
        if (!ProductRules.IsValidType(request.ProductType))
            return Result.Failure<MockupTemplateResponseDto>(Error.Validation("Choose a supported product type."));
        if (request.Attributes is null)
            return Result.Failure<MockupTemplateResponseDto>(Error.Validation("Choose who is in the photo."));
        if (ModelPeople.Problem(request.Attributes) is { } problem)
            return Result.Failure<MockupTemplateResponseDto>(Error.Validation(problem));
        if (imageProviders.FirstOrDefault(provider => provider.Provider == ImageGenerationProviders.Ideogram) is not { } ideogram)
            return Result.Failure<MockupTemplateResponseDto>(Error.Failure("ModelPhotoGenerationUnavailable", "Making model photos with AI is not available."));

        var key = (await keys.ListOwnedAsync(userId, cancellationToken)).FirstOrDefault(x =>
            x.ServiceProvider == ImageGenerationProviders.Ideogram && x.IsActive == true && x.LastCheckSucceeded == true);
        if (key is null)
            return Result.Failure<MockupTemplateResponseDto>(Error.Conflict("MissingApiKey", "Connect a valid Ideogram API key before making a model photo with AI."));

        var (prompt, aspectRatio) = ModelPhotoPrompts.Build(request.Attributes);
        ImageGenerationResult generated;
        try
        {
            generated = await ideogram.GenerateAsync(credentials.Unprotect(key.KeyValueEncrypted), prompt, aspectRatio, cancellationToken: cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or CryptographicException)
        {
            logger.LogWarning(error, "A model photo could not be made with AI");
            return Result.Failure<MockupTemplateResponseDto>(Failed());
        }
        if (!generated.IsSuccess)
        {
            logger.LogWarning("A model photo was not made with AI: {Reason}", generated.ErrorMessage);
            return Result.Failure<MockupTemplateResponseDto>(generated.IsBlocked
                ? Error.Validation("Ideogram declined to make this photo. Try again, or choose other people.")
                : Failed());
        }

        // Ideogram has charged for it, whatever happens to the photo next.
        var now = time.GetUtcNow().UtcDateTime;
        await usage.AddAsync(new ApiUsageRecord
        {
            Id = Guid.NewGuid(), UserId = userId, Provider = ImageGenerationProviders.Ideogram, Feature = "image_generation",
            ModelName = generated.ModelUsed, RequestUnits = 1, CostUsd = generated.EstimatedCostUsd, Status = "success", CreatedAt = now
        }, saveChange: true, cancellationToken);

        // A 2K PNG photo can be over the upload limit; as a JPEG it is a fraction of it and looks the same.
        byte[] bytes;
        try
        {
            bytes = await maps.ToJpegAsync(generated.ImageBytes!, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(error, "The photo made with AI could not be read");
            return Result.Failure<MockupTemplateResponseDto>(Failed());
        }
        var name = $"AI · {ModelPeople.Label(request.Attributes)} · {Guid.NewGuid().ToString("N")[..6]}";
        using var content = new MemoryStream(bytes);
        var created = await modelPhotos.CreateAsync(new SaveModelPhotoRequestDto(
            name, request.ProductType, 0, 0, 1, 1,
            new UploadFileDto("model.jpg", "image/jpeg", bytes.Length, content),
            GarmentSelection: null, AutoPlacement: true, FindGarment: true, Attributes: request.Attributes), system, cancellationToken);
        if (created.IsFailure)
        {
            logger.LogWarning("A model photo made with AI could not be saved: {Reason}", created.Error.Message);
            return created;
        }

        // Marked as made by AI, with a print area on the chest for when a garment cannot be measured.
        var template = await templates.GetByIdAsync(created.Value.Id, cancellationToken);
        if (template is null)
            return created;
        template.AiGenerated = true;
        template.PrintAreaConfig = MockupRules.SerializePrintArea(ChestArea(template.OutputWidthPx, template.OutputHeightPx), automatic: true);
        template.UpdatedAt = now;
        await templates.UpdateAsync(template, saveChange: true, cancellationToken);
        return Result.Success(created.Value with { AiGenerated = true, PrintAreaConfig = template.PrintAreaConfig });
    }

    // The middle of the photo's upper half, where a chest print sits on a person framed head to mid-thigh.
    internal static MockupPosition ChestArea(int width, int height) =>
        new((int)(width * 0.35), (int)(height * 0.3), Math.Max(1, (int)(width * 0.3)), Math.Max(1, (int)(height * 0.25)));

    private static Error Failed() => Error.Failure("ModelPhotoGenerationFailed", "Ideogram could not make the photo. Try again.");
}
