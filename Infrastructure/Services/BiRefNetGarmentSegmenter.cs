using System.Security.Cryptography;
using System.Runtime.InteropServices;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace APCS.Infrastructure.Services;

/// <summary>Says, per pixel, how much of it belongs to the main object of a photo.</summary>
internal interface IGarmentSegmenter
{
    /// <summary>Whether the model is installed and could be loaded.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The object's matte (0..255) at <paramref name="width"/> x <paramref name="height"/>, or
    /// <see langword="null"/> when the model is not available or it failed on this photo.
    /// </summary>
    /// <param name="photo">The photo.</param>
    /// <param name="width">The width to return the matte at.</param>
    /// <param name="height">The height to return the matte at.</param>
    /// <param name="quick">A faster, slightly coarser answer, for a preview.</param>
    byte[]? Segment(Image<Rgba32> photo, int width, int height, bool quick = false);
}

/// <summary>
/// Runs the BiRefNet (lite) matting model. Its matte follows the object's edge closely and
/// leaves out its cast shadow, so it is used as it comes.
/// </summary>
internal sealed class BiRefNetGarmentSegmenter : IGarmentSegmenter, IDisposable
{
    // The side the model sees the photo at. The quick side takes about a quarter of the time and
    // memory, and its matte differs from the full one in well under 1% of the pixels.
    private const int ModelSize = 1024;
    private const int QuickModelSize = 512;
    private const int RememberedMattes = 6;
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Deviation = [0.229f, 0.224f, 0.225f];

    private readonly Lazy<InferenceSession?> _session;
    private readonly ILogger<BiRefNetGarmentSegmenter> _logger;
    // One photo at a time: a run takes seconds of every core and hundreds of MB.
    private readonly object _running = new();
    // The same photo at the same size is often asked for again (a preview repeated, a save retried).
    private readonly LinkedList<(string Key, byte[] Matte)> _recent = new();
    private volatile bool _disposed;

    public BiRefNetGarmentSegmenter(IOptions<MockupOptions> options, IHostEnvironment environment, ILogger<BiRefNetGarmentSegmenter> logger)
    {
        _logger = logger;
        _session = new Lazy<InferenceSession?>(() =>
        {
            var path = ResolvePath(options.Value.SegmentationModelPath, environment.ContentRootPath);
            if (path is null)
            {
                logger.LogWarning(
                    "Segmentation model '{Path}' was not found; mock-up photos cannot be processed. Restart to download it again, or run scripts/Get-SegmentationModel.ps1.",
                    options.Value.SegmentationModelPath);
                return null;
            }

            try
            {
                // A run needs more than a GB; without the arena that memory goes back to the system
                // afterwards instead of staying reserved for the next run.
                using var sessionOptions = new SessionOptions { EnableCpuMemArena = false, EnableMemoryPattern = false };
                return new InferenceSession(path, sessionOptions);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Segmentation model '{Path}' could not be loaded; mock-up photos cannot be processed.", path);
                return null;
            }
        });
    }

    public bool IsAvailable => _session.Value is not null;

    public byte[]? Segment(Image<Rgba32> photo, int width, int height, bool quick = false)
    {
        if (_session.Value is not { } session)
            return null;

        var size = quick ? QuickModelSize : ModelSize;
        var (pixels, key) = ModelInput(photo, size);
        var matte = MatteOf(session, pixels, key, size);
        if (matte is null)
            return null;

        // A preview is usually followed by saving the same photo, which needs the full matte. It is
        // worked out in the background meanwhile, so the save finds it ready instead of waiting.
        if (quick)
        {
            var (fullPixels, fullKey) = ModelInput(photo, ModelSize);
            _ = Task.Run(() => MatteOf(session, fullPixels, fullKey, ModelSize));
        }

        if (width == size && height == size) return (byte[])matte.Clone();
        using var map = Image.LoadPixelData<L8>(matte, size, size);
        map.Mutate(c => c.Resize(width, height));
        var result = new byte[width * height];
        map.CopyPixelDataTo(result);
        return result;
    }

    private static (Rgba32[] Pixels, string Key) ModelInput(Image<Rgba32> photo, int size)
    {
        using var small = photo.Clone(c => c.Resize(size, size));
        var pixels = new Rgba32[size * size];
        small.CopyPixelDataTo(pixels);
        return (pixels, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(pixels.AsSpan()))));
    }

    // The matte for one model input: remembered when it was worked out before, otherwise run now.
    // A caller that finds a run for the same input in progress waits for it and gets its result.
    private byte[]? MatteOf(InferenceSession session, Rgba32[] pixels, string key, int size)
    {
        lock (_running)
        {
            if (_disposed)
                return null;

            var matte = _recent.FirstOrDefault(entry => entry.Key == key).Matte;
            if (matte is not null)
                return matte;

            try
            {
                matte = Run(session, pixels, size);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The segmentation model failed on a photo.");
                return null;
            }

            _recent.AddFirst((key, matte));
            if (_recent.Count > RememberedMattes) _recent.RemoveLast();
            return matte;
        }
    }

    private static byte[] Run(InferenceSession session, Rgba32[] pixels, int size)
    {
        var input = new DenseTensor<float>([1, 3, size, size]);
        for (var i = 0; i < pixels.Length; i++)
        {
            int x = i % size, y = i / size;
            input[0, 0, y, x] = (pixels[i].R / 255f - Mean[0]) / Deviation[0];
            input[0, 1, y, x] = (pixels[i].G / 255f - Mean[1]) / Deviation[1];
            input[0, 2, y, x] = (pixels[i].B / 255f - Mean[2]) / Deviation[2];
        }

        using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
        // The last output is the full-resolution prediction, as logits.
        var output = results[^1].AsTensor<float>();
        var matte = new byte[pixels.Length];
        for (var i = 0; i < matte.Length; i++)
            matte[i] = ImageMaskOps.ToByte(255.0 / (1.0 + Math.Exp(-output[0, 0, i / size, i % size])));
        return matte;
    }

    public void Dispose()
    {
        _disposed = true;
        // Waits for a run in progress, so the session is not pulled from under it.
        lock (_running)
        {
            if (_session.IsValueCreated) _session.Value?.Dispose();
        }
    }

    internal static string? ResolvePath(string configured, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        if (Path.IsPathRooted(configured)) return File.Exists(configured) ? configured : null;

        var parent = Path.GetDirectoryName(contentRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return new[] { contentRoot, parent }
            .Where(root => !string.IsNullOrEmpty(root))
            .Select(root => Path.GetFullPath(Path.Combine(root!, configured)))
            .FirstOrDefault(File.Exists);
    }
}
