using APCS.Infrastructure.Options;
using CloudinaryDotNet;

namespace APCS.Infrastructure.Services;

/// <summary>Builds the shared Cloudinary client from options.</summary>
internal static class CloudinaryClientFactory
{
    internal static Cloudinary Create(CloudinaryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Cloudinary storage is not configured. Set Cloudinary__CloudName, Cloudinary__ApiKey, and Cloudinary__ApiSecret.");
        }

        return new Cloudinary(new Account(
            options.CloudName,
            options.ApiKey,
            options.ApiSecret))
        {
            Api = { Secure = true }
        };
    }
}
