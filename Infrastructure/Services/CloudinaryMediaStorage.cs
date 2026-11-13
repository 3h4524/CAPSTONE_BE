using System.Security.Cryptography;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Workflows;
using APCS.Infrastructure.Options;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

public sealed class CloudinaryMediaStorage(IOptions<CloudinaryOptions> options, TimeProvider time, IHttpClientFactory clients,
    IMemoryCache? cache = null) : IMediaStorage
{
    // A signed read link lives ten minutes. The same one is handed out for five: a page that reads its images again
    // (a run's mock-ups, refreshed by every change of the run) then gets identical links, and the browser keeps the
    // images it has instead of downloading every one again. Each link still has at least five minutes left.
    private static readonly TimeSpan ReuseSignedLinkFor = TimeSpan.FromMinutes(5);

    private Cloudinary Client => CloudinaryClientFactory.Create(options.Value);
    public async Task<StoredMockup> UploadMockupAsync(Stream content, string fileName, string key, CancellationToken ct)
    {
        using var description = new FileDescription(fileName, content);
        return await StoreMockupAsync(description, key, ct);
    }
    // Cloudinary fetches the URL itself, so the image never passes through this server.
    public Task<StoredMockup> UploadMockupFromUrlAsync(string sourceUrl, string key, CancellationToken ct) =>
        StoreMockupAsync(new FileDescription(sourceUrl), key, ct);
    private async Task<StoredMockup> StoreMockupAsync(FileDescription description, string key, CancellationToken ct)
    {
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
    public Task<byte[]> ReadImageAsync(string key, string? version, CancellationToken ct) =>
        clients.CreateClient("PrivateMedia").GetByteArrayAsync(SignRead(key, "image", version), ct);
    public Task<byte[]> ReadPublicImageAsync(string key, CancellationToken ct) =>
        clients.CreateClient("PrivateMedia").GetByteArrayAsync(Client.Api.UrlImgUp.Secure(true).Format("png").BuildUrl(PublicId(key)), ct);
    // Only this account's images are fetched: the URL comes from a stored mock-up, never straight from a request.
    public Task<byte[]> ReadCompositeAsync(string url, CancellationToken ct) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "res.cloudinary.com"
            && uri.AbsolutePath.StartsWith($"/{options.Value.CloudName}/image/", StringComparison.Ordinal)
            ? clients.CreateClient("PrivateMedia").GetByteArrayAsync(uri, ct)
            : throw new InvalidOperationException("Not an image of this Cloudinary account.");
    public string SignRead(string key, string type, string? version = null, bool attachment = false) =>
        cache is null
            ? SignReadNow(key, type, attachment)
            : cache.GetOrCreate(("signed-read", options.Value.CloudName, key, type, version, attachment), entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = ReuseSignedLinkFor;
                return SignReadNow(key, type, attachment);
            })!;
    private string SignReadNow(string key, string type, bool attachment, Transformation? transformation = null) => Client.DownloadPrivate(
        PublicId(key), attachment: attachment, format: type == "image" ? (transformation is null ? "png" : "jpg") : type == "video" ? "mp4" : null,
        type: "authenticated", expiresAt: time.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds(), resourceType: type,
        transformation: transformation, targetFilename: type == "video" ? "video.mp4" : type == "raw" ? "video-package.zip" : null);
    public string SignReadFitted(string key, string? version, int width, int height, bool attachment = false)
    {
        // Padded, never cropped: a mock-up's design stays whole on a taller social format.
        var fit = new Transformation().Width(width).Height(height).Crop("pad").Background("auto").Quality("auto:good");
        return cache is null
            ? SignReadNow(key, "image", attachment, fit)
            : cache.GetOrCreate(("signed-read-fitted", options.Value.CloudName, key, version, width, height, attachment), entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = ReuseSignedLinkFor;
                return SignReadNow(key, "image", attachment, fit);
            })!;
    }
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
