using APCS.Application.Abstractions.AI;
using APCS.Application.Features.Workflows;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace APCS.Infrastructure.Services;

/// <summary>Shared MiniMax PNG download, crop and resize policy; analysis sees the provider's first frame.</summary>
public sealed class MiniMaxFirstFramePreparer(IHttpClientFactory clients) : IVideoFirstFramePreparer
{
    private const int MaxSourceBytes = 20_000_000;
    private const int MaxPreparedBytes = 30_000_000;

    public async Task<byte[]> PrepareAsync(string sourceImageUrl, string aspectRatio, Region? sourceCrop,
        CancellationToken ct)
    {
        using var response = await clients.CreateClient("PrivateMedia")
            .GetAsync(sourceImageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
        var source = await ReadLimitedAsync(sourceStream, MaxSourceBytes,
            "AI video source exceeds 20 MB.", ct);
        if (source.Length < 24 || !source.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidOperationException("AI video source must be a valid approved PNG under 20 MB.");

        using var image = Image.Load(source);
        if (sourceCrop is not null) image.Mutate(x => x.Crop(ToPixelCrop(sourceCrop, image.Width, image.Height)));
        image.Mutate(x => x.Crop(CentreCrop(image.Width, image.Height, aspectRatio)));

        var scale = Math.Min(1d, 5760d / Math.Max(image.Width, image.Height));
        if (Math.Min(image.Width, image.Height) * scale < 256)
            scale = 256d / Math.Min(image.Width, image.Height);
        if (Math.Abs(scale - 1d) > 0.0001)
            image.Mutate(x => x.Resize(Math.Max(1, (int)Math.Round(image.Width * scale)),
                Math.Max(1, (int)Math.Round(image.Height * scale))));
        if (image.Width > 5760 || image.Height > 5760)
            throw new InvalidOperationException("The approved source crop is outside MiniMax image limits.");

        await using var prepared = new MemoryStream();
        await image.SaveAsPngAsync(prepared, ct);
        if (prepared.Length > MaxPreparedBytes)
            throw new InvalidOperationException("The prepared MiniMax first frame exceeds 30 MB.");
        return prepared.ToArray();
    }

    private static Rectangle ToPixelCrop(Region crop, int width, int height)
    {
        var x = Math.Clamp((int)Math.Floor(crop.X * width), 0, width - 1);
        var y = Math.Clamp((int)Math.Floor(crop.Y * height), 0, height - 1);
        var cropWidth = Math.Clamp((int)Math.Ceiling(crop.Width * width), 1, width - x);
        var cropHeight = Math.Clamp((int)Math.Ceiling(crop.Height * height), 1, height - y);
        return new Rectangle(x, y, cropWidth, cropHeight);
    }

    private static Rectangle CentreCrop(int width, int height, string aspectRatio)
    {
        var targetRatio = aspectRatio == "9:16" ? 9d / 16d : 16d / 9d;
        var currentRatio = (double)width / height;
        if (currentRatio > targetRatio)
        {
            var targetWidth = Math.Max(1, (int)Math.Round(height * targetRatio));
            return new Rectangle((width - targetWidth) / 2, 0, targetWidth, height);
        }
        var targetHeight = Math.Max(1, (int)Math.Round(width / targetRatio));
        return new Rectangle(0, (height - targetHeight) / 2, width, targetHeight);
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int limit, string message, CancellationToken ct)
    {
        await using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (memory.Length + count > limit) throw new InvalidOperationException(message);
            await memory.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return memory.ToArray();
    }
}
