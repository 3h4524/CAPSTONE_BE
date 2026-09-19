using APCS.Application.Abstractions.Storage;
using APCS.Infrastructure.Options;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>Stores support attachments as authenticated Cloudinary raw assets.</summary>
public sealed class CloudinaryFileStorageService : IFileStorageService
{
    private readonly Cloudinary _cloudinary;
    private readonly CloudinaryOptions _options;
    private readonly TimeProvider _timeProvider;

    public CloudinaryFileStorageService(
        IOptions<CloudinaryOptions> options,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;

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

    public async Task<StoredFileDto> UploadAsync(
        UploadFileDto file,
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        var publicId = CloudinaryPaths.BuildPublicId(_options.Folder, storageKey);
        var assetFolder = CloudinaryPaths.BuildAssetFolder(_options.Folder, storageKey);
        using var description = new FileDescription(file.FileName, file.Content);
        var result = await _cloudinary.UploadAsync(
            new RawUploadParams
            {
                File = description,
                PublicId = publicId,
                AssetFolder = assetFolder,
                Type = "authenticated",
                Overwrite = false,
                UseFilename = false,
                UniqueFilename = false,
                DiscardOriginalFilename = true
            },
            cancellationToken: cancellationToken);

        if (result.Error is not null || result.SecureUrl is null)
        {
            throw new InvalidOperationException(
                $"Cloudinary could not store the attachment: {result.Error?.Message ?? "No delivery URL was returned."}");
        }

        return new StoredFileDto(storageKey, result.SecureUrl.AbsoluteUri);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _cloudinary.DestroyAsync(new DeletionParams(CloudinaryPaths.BuildPublicId(_options.Folder, storageKey))
        {
            ResourceType = ResourceType.Raw,
            Type = "authenticated",
            Invalidate = true
        });

        if (result.Error is not null)
        {
            throw new InvalidOperationException($"Cloudinary could not delete the attachment: {result.Error.Message}");
        }
    }

    public SignedDownloadDto CreateSignedDownload(string storageKey, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var expiresAt = _timeProvider.GetUtcNow().AddMinutes(_options.SignedUrlTtlMinutes);
        var url = _cloudinary.DownloadPrivate(
            CloudinaryPaths.BuildPublicId(_options.Folder, storageKey),
            attachment: true,
            format: null,
            type: "authenticated",
            expiresAt: expiresAt.ToUnixTimeSeconds(),
            resourceType: "raw",
            transformation: null,
            targetFilename: Path.GetFileName(fileName));

        return new SignedDownloadDto(url, expiresAt);
    }
}
