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

        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Cloudinary storage is not configured. Set Cloudinary__CloudName, Cloudinary__ApiKey, and Cloudinary__ApiSecret.");
        }

        _cloudinary = new Cloudinary(new Account(
            _options.CloudName,
            _options.ApiKey,
            _options.ApiSecret))
        {
            Api = { Secure = true }
        };
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
                PublicId = BuildPublicId(storageKey),
                AssetFolder = BuildAssetFolder(storageKey),
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

        var result = await _cloudinary.DestroyAsync(new DeletionParams(BuildPublicId(storageKey))
        {
            ResourceType = ResourceType.Image,
            Invalidate = true
        });

        if (result.Error is not null && result.Error.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (result.Error is not null)
        {
            throw new InvalidOperationException($"Cloudinary could not delete the image: {result.Error.Message}");
        }
    }

    private string BuildPublicId(string storageKey)
    {
        var folder = NormalizePath(_options.Folder);
        var key = NormalizePath(storageKey);
        return string.IsNullOrWhiteSpace(folder) ? key : $"{folder}/{key}";
    }

    private string BuildAssetFolder(string storageKey)
    {
        var publicId = BuildPublicId(storageKey);
        var lastSeparatorIndex = publicId.LastIndexOf('/');
        return lastSeparatorIndex < 0 ? string.Empty : publicId[..lastSeparatorIndex];
    }

    private static string NormalizePath(string value) =>
        string.Join('/', value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
