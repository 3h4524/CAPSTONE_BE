using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

/// <summary>Tells the clothes on a person apart from their skin, hair and the rest of them.</summary>
internal interface IClothesSegmenter
{
    /// <summary>Whether the model is installed and could be loaded.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// How likely (0..255) each pixel is the garment worn on the upper body (a tee, a shirt, a hoodie), at
    /// <paramref name="width"/> x <paramref name="height"/>; <see langword="null"/> when the model is not available
    /// or failed on this photo.
    /// </summary>
    byte[]? UpperClothes(Image<Rgba32> photo, int width, int height);
}

/// <summary>
/// Runs SegFormer B2 fine-tuned on clothes (ATR labels; "Upper-clothes" is label 4). It knows a shirt from the
/// arms, face and trousers of the person wearing it, which the object matte (BiRefNet) cannot tell apart.
/// Its answer is coarse (a quarter of its input), so the edge against the background comes from the matte.
/// </summary>
/// <remarks>
/// The weights are licensed for non-commercial use only (NVIDIA's SegFormer license); replace the model before
/// any commercial use.
/// </remarks>
internal sealed class SegFormerClothesSegmenter : IClothesSegmenter, IDisposable
{
    private const int ModelSize = 512;
    private const int UpperClothesLabel = 4;
    private const int RememberedAnswers = 4;
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Deviation = [0.229f, 0.224f, 0.225f];

    private readonly Lazy<InferenceSession?> _session;
    private readonly ILogger<SegFormerClothesSegmenter> _logger;
    private readonly object _running = new();
    // A preview is followed by saving the same photo, which asks again.
    private readonly LinkedList<(string Key, float[] Probability, int Width, int Height)> _recent = new();
    private volatile bool _disposed;

    public SegFormerClothesSegmenter(IOptions<MockupOptions> options, IHostEnvironment environment, ILogger<SegFormerClothesSegmenter> logger)
    {
        _logger = logger;
        _session = new Lazy<InferenceSession?>(() =>
        {
            var path = BiRefNetGarmentSegmenter.ResolvePath(options.Value.ClothesModelPath, environment.ContentRootPath);
            if (path is null)
            {
                logger.LogWarning(
                    "Clothes model '{Path}' was not found; the garment on a model photo is selected by hand. Restart to download it again.",
                    options.Value.ClothesModelPath);
                return null;
            }

            try
            {
                using var sessionOptions = new SessionOptions { EnableCpuMemArena = false, EnableMemoryPattern = false };
                return new InferenceSession(path, sessionOptions);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Clothes model '{Path}' could not be loaded; the garment on a model photo is selected by hand.", path);
                return null;
            }
        });
    }

    public bool IsAvailable => _session.Value is not null;

    public byte[]? UpperClothes(Image<Rgba32> photo, int width, int height)
    {
        if (_session.Value is not { } session)
            return null;

        using var small = photo.Clone(c => c.Resize(ModelSize, ModelSize));
        var pixels = new Rgba32[ModelSize * ModelSize];
        small.CopyPixelDataTo(pixels);
        var key = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(pixels.AsSpan())));

        (float[] Probability, int Width, int Height)? answer;
        lock (_running)
        {
            if (_disposed)
                return null;
            var known = _recent.FirstOrDefault(entry => entry.Key == key);
            if (known.Probability is not null)
                answer = (known.Probability, known.Width, known.Height);
            else
            {
                try
                {
                    answer = Run(session, pixels);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "The clothes model failed on a photo.");
                    return null;
                }
                _recent.AddFirst((key, answer.Value.Probability, answer.Value.Width, answer.Value.Height));
                if (_recent.Count > RememberedAnswers) _recent.RemoveLast();
            }
        }

        // Scaled up smoothly, so the garment's edge against an arm is a curve rather than blocks.
        var (probability, w, h) = answer.Value;
        using var map = Image.LoadPixelData<L8>(probability.Select(value => ImageMaskOps.ToByte(value * 255)).ToArray(), w, h);
        map.Mutate(c => c.Resize(width, height, KnownResamplers.Triangle));
        var result = new byte[width * height];
        map.CopyPixelDataTo(result);
        return result;
    }

    // The chance of the upper-body garment in each cell of the model's answer: a softmax over its labels.
    private static (float[] Probability, int Width, int Height) Run(InferenceSession session, Rgba32[] pixels)
    {
        var input = new DenseTensor<float>([1, 3, ModelSize, ModelSize]);
        for (var i = 0; i < pixels.Length; i++)
        {
            int x = i % ModelSize, y = i / ModelSize;
            input[0, 0, y, x] = (pixels[i].R / 255f - Mean[0]) / Deviation[0];
            input[0, 1, y, x] = (pixels[i].G / 255f - Mean[1]) / Deviation[1];
            input[0, 2, y, x] = (pixels[i].B / 255f - Mean[2]) / Deviation[2];
        }

        using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
        var logits = results[0].AsTensor<float>();
        var (labels, height, width) = (logits.Dimensions[1], logits.Dimensions[2], logits.Dimensions[3]);
        var probability = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var top = float.MinValue;
                for (var label = 0; label < labels; label++) top = Math.Max(top, logits[0, label, y, x]);
                double sum = 0;
                for (var label = 0; label < labels; label++) sum += Math.Exp(logits[0, label, y, x] - top);
                probability[y * width + x] = (float)(Math.Exp(logits[0, UpperClothesLabel, y, x] - top) / sum);
            }
        }
        return (probability, width, height);
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_running)
        {
            if (_session.IsValueCreated) _session.Value?.Dispose();
        }
    }
}
