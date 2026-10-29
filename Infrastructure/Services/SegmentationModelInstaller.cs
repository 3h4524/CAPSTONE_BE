using System.Security.Cryptography;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Downloads the segmentation model on startup when it is not on this machine yet, so a fresh
/// checkout or deployment needs no manual step, and then loads it. The application starts once the
/// file is in place; if the download fails it starts all the same, and mock-up photos cannot be
/// processed until the model is there.
/// </summary>
internal sealed class SegmentationModelInstaller(
    IHttpClientFactory clients,
    IOptions<MockupOptions> options,
    IHostEnvironment environment,
    IGarmentSegmenter segmenter,
    ILogger<SegmentationModelInstaller> logger) : IHostedService
{
    internal const string HttpClientName = "SegmentationModel";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await InstallIfMissingAsync(cancellationToken);
        // Loading the model takes several seconds. Done now, in the background, startup does not
        // wait for it and neither does the first photo.
        _ = Task.Run(() => segmenter.IsAvailable, CancellationToken.None);
    }

    private async Task InstallIfMissingAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.SegmentationModelUrl) || string.IsNullOrWhiteSpace(settings.SegmentationModelPath))
            return;
        if (BiRefNetGarmentSegmenter.ResolvePath(settings.SegmentationModelPath, environment.ContentRootPath) is not null)
            return;

        var target = InstallPath(settings.SegmentationModelPath, environment.ContentRootPath);
        var temporary = target + ".download";
        try
        {
            logger.LogInformation(
                "Segmentation model not found; downloading it to '{Target}' (about 180 MB, once). The application starts when this is done.", target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await DownloadAsync(settings.SegmentationModelUrl, settings.SegmentationModelSha256, temporary, cancellationToken);
            File.Move(temporary, target, overwrite: true);
            logger.LogInformation("Segmentation model installed.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex,
                "The segmentation model could not be downloaded; mock-up photos cannot be processed until it is installed (restart to retry, or run scripts/Get-SegmentationModel.ps1).");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Where a missing model is put: beside the solution when running from a checkout (that folder
    /// is ignored by Git), otherwise under the content root.
    /// </summary>
    internal static string InstallPath(string configured, string contentRoot)
    {
        if (Path.IsPathRooted(configured)) return configured;

        var parent = Path.GetDirectoryName(contentRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var root = !string.IsNullOrEmpty(parent) && Directory.EnumerateFiles(parent, "*.sln").Any() ? parent : contentRoot;
        return Path.GetFullPath(Path.Combine(root, configured));
    }

    private async Task DownloadAsync(string url, string expectedSha256, string file, CancellationToken cancellationToken)
    {
        using var response = await clients.CreateClient(HttpClientName).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(file))
        {
            var buffer = new byte[1 << 20];
            long done = 0;
            var reported = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;

                if (total is not > 0) continue;
                var quarter = (int)(done * 4 / total.Value);
                if (quarter <= reported || quarter >= 4) continue;
                reported = quarter;
                logger.LogInformation("Segmentation model: {Percent}% downloaded.", quarter * 25);
            }
        }

        var actual = Convert.ToHexString(hash.GetHashAndReset());
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The downloaded file has an unexpected checksum ({actual.ToLowerInvariant()}); nothing was installed.");
    }
}
