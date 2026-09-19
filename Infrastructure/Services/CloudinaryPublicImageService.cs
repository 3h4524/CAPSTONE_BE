using APCS.Application.Abstractions.Storage;
using APCS.Infrastructure.Options;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>Stores publicly readable images as Cloudinary image assets.</summary>
public sealed class CloudinaryPublicImageService : IPublicImageService
{
    private readonly Cloudinary _cloudinary;
    private readonly CloudinaryOptions _options;

    public CloudinaryPublicImageService(IOptions<CloudinaryOptions> options)
    {
        _options = options.Value;
        _cloudinary = CloudinaryClientFactory.Create(_options);
    }

    public async Task<string> UploadImageAsync(
        UploadFileDto file,
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        using var description = new FileDescription(file.FileName, file.Content);
        var result = await _cloudinary.UploadAsync(
            new ImageUploadParams
            {
                File = description,
                PublicId = CloudinaryPaths.BuildPublicId(_options.Folder, storageKey),
                AssetFolder = CloudinaryPaths.BuildAssetFolder(_options.Folder, storageKey),
                Overwrite = true,
                UseFilename = false,
                UniqueFilename = false,
                DiscardOriginalFilename = true
            },
            cancellationToken: cancellationToken);

        if (result.Error is not null || result.SecureUrl is null)
        {
            throw new InvalidOperationException(
                $"Cloudinary could not store the image: {result.Error?.Message ?? "No delivery URL was returned."}");
        }

        return result.SecureUrl.AbsoluteUri;
    }

    public async Task DeleteImageAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _cloudinary.DestroyAsync(new DeletionParams(CloudinaryPaths.BuildPublicId(_options.Folder, storageKey))
        {
            ResourceType = ResourceType.Image,
            Invalidate = true
        });

        if (string.Equals(result.Result, "not found", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (result.Error is not null)
        {
            throw new InvalidOperationException($"Cloudinary could not delete the image: {result.Error.Message}");
        }
    }
}
