using APCS.Application.Abstractions.Storage;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Builds a Cloudinary "on-the-fly" overlay transformation URL: splices a transformation component
/// into an existing delivery URL right after "/upload/", combining two already-uploaded public IDs
/// (the mock-up base photo and the design image) without any new upload/HTTP call.
/// </summary>
/// <remarks>
/// This is built as plain string formatting of Cloudinary's classical transformation syntax
/// (<c>l_&lt;overlay&gt;,g_north_west,x_..,y_..,w_..,h_..,c_fit,fl_layer_apply</c>) rather than through
/// the CloudinaryDotNet SDK's fluent <c>Transformation</c>/<c>Layer</c> builder, so the exact output is
/// deterministic and easy to unit test.
/// </remarks>
public sealed class CloudinaryMockupCompositor(IOptions<CloudinaryOptions> options) : IMockupCompositor
{
    private const string UploadMarker = "/upload/";
    private readonly CloudinaryOptions _options = options.Value;

    public string BuildCompositeUrl(string baseImageUrl, string overlayStorageKey, MockupPosition position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseImageUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayStorageKey);
        ArgumentNullException.ThrowIfNull(position);

        var markerIndex = baseImageUrl.IndexOf(UploadMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException(
                $"'{baseImageUrl}' does not look like a Cloudinary delivery URL (missing '{UploadMarker}').");
        }

        // The overlay's real Cloudinary public id includes the same root folder every upload goes
        // through (see CloudinaryPaths.BuildPublicId) — a bare storage key alone 404s. Cloudinary's
        // classical transformation syntax then requires "/" to be encoded as ":" once embedded inside
        // a transformation component (it would otherwise be read as another path segment).
        var overlayPublicId = CloudinaryPaths.BuildPublicId(_options.Folder, overlayStorageKey);
        var overlayToken = overlayPublicId.Replace('/', ':');

        var segment = string.Join(',',
        [
            $"l_{overlayToken}",
            "g_north_west",
            $"x_{position.X}",
            $"y_{position.Y}",
            $"w_{position.Width}",
            $"h_{position.Height}",
            "c_fit",
            "fl_layer_apply"
        ]);

        return baseImageUrl.Insert(markerIndex + UploadMarker.Length, segment + "/");
    }
}
