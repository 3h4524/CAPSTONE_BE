using System.Security.Cryptography;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Workflows;
using APCS.Infrastructure.Options;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

public sealed class CloudinaryMediaStorage(IOptions<CloudinaryOptions> options, TimeProvider time, IHttpClientFactory clients) : IMediaStorage
{
    private Cloudinary Client => CloudinaryClientFactory.Create(options.Value);
    public async Task<StoredMockup> UploadMockupAsync(Stream content, string fileName, string key, CancellationToken ct)
    {
        using var description = new FileDescription(fileName, content);
        var result = await Client.UploadAsync(new ImageUploadParams
        {
            File = description, PublicId = PublicId(key), Type = "authenticated", Overwrite = false,
            Format = "png", Transformation = new Transformation().Angle("auto").Flags("strip_profile")
        }, cancellationToken: ct);
        if (result.Error != null || result.Width <= 0 || result.Height <= 0 || (long)result.Width * result.Height > 32_000_000)
            throw new InvalidOperationException("Invalid image or image exceeds 32 megapixels.");
        using var response = await clients.CreateClient("PrivateMedia").GetAsync(SignRead(key, "image"), ct);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new(key, result.Version, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.Width, result.Height);
    }
    public string SignRead(string key, string type, string? version = null, bool attachment = false) => Client.DownloadPrivate(
        PublicId(key), attachment: attachment, format: type == "image" ? "png" : type == "video" ? "mp4" : null,
        type: "authenticated", expiresAt: time.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds(), resourceType: type,
        transformation: null, targetFilename: type == "video" ? "video.mp4" : type == "raw" ? "video-package.zip" : null);
    public UploadGrant CreateUploadGrant(string key, string type)
    {
        var fields = new Dictionary<string, string>
        {
            ["public_id"] = PublicId(key), ["type"] = "authenticated", ["overwrite"] = "false",
            ["timestamp"] = time.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        fields["signature"] = Client.Api.SignParameters(new SortedDictionary<string, object>(fields.ToDictionary(x => x.Key, x => (object)x.Value), StringComparer.Ordinal));
        fields["api_key"] = options.Value.ApiKey;
        return new($"https://api.cloudinary.com/v1_1/{options.Value.CloudName}/{type}/upload", fields, key, type);
    }
    public async Task<bool> VerifyAsync(string key, string type, string version, long? bytes, CancellationToken ct)
    {
        var result = await Client.GetResourceAsync(new GetResourceParams(PublicId(key))
        { ResourceType = type == "video" ? ResourceType.Video : type == "raw" ? ResourceType.Raw : ResourceType.Image, Type = "authenticated" }, ct);
        return result.Error == null && result.Version == version && (!bytes.HasValue || result.Bytes == bytes.Value);
    }
    private string PublicId(string key) => CloudinaryPaths.BuildPublicId(options.Value.Folder, key);
}
