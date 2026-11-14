using System.Security.Cryptography;
using System.Text;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.BatchMockups.Printify;

/// <summary>
/// Makes shared mock-up templates of Printify products, without AI and without uploading photos. A background job
/// prints a magenta sheet on a throwaway product (the magenta on its mock-ups is the print area) and a bare one (the
/// photo of the template, and the photos of each garment color), reads them and deletes both products. The admin
/// then picks a photo per side and the template, its sides, its Printify link and its colors are saved together.
/// </summary>
public interface IPrintifyTemplateBuilder
{
    /// <summary>Starts measuring a Printify product for a new template.</summary>
    Task<Result<PrintifyTemplateJobDto>> StartAsync(StartPrintifyTemplateJobRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Starts measuring more sides or colors of a shared template already linked to Printify.</summary>
    Task<Result<PrintifyTemplateJobDto>> StartForTemplateAsync(Guid templateId, StartPrintifyTemplateUpdateRequestDto request, CancellationToken cancellationToken = default);

    Task<Result<PrintifyTemplateJobDto>> GetAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Runs a queued job; called by the background worker. Failures end up on the job, never thrown.</summary>
    Task RunAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Saves the template (or the new sides and colors of a linked one) from a finished job, all or nothing.</summary>
    Task<Result<MockupTemplateResponseDto>> CreateAsync(Guid jobId, CreatePrintifyTemplateRequestDto request, CancellationToken cancellationToken = default);
}

public sealed class PrintifyTemplateBuilder(
    ICurrentUser currentUser,
    IPrintifyClient printify,
    IPrintifyAdminToken adminToken,
    PrintifyAdminCatalog catalog,
    PrintifyColorMapService colorMap,
    IPrintifyMockupCalibrator calibrator,
    IPrintifyTemplateJobQueue queue,
    ICacheService cache,
    IPublicImageService images,
    IMockupMapService mapService,
    IMockupTemplateService templateService,
    IRepository<MockupTemplate> templates,
    IRepository<MockupTemplatePrintifyLink> links,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<PrintifyTemplateBuilder> logger) : IPrintifyTemplateBuilder
{
    internal const string Queued = "queued";
    internal const string Running = "running";
    internal const string Done = "done";
    internal const string Failed = "failed";

    /// <summary>The title of the throwaway products, so one left by a stopped server is easy to find and delete.</summary>
    internal const string ProbeTitle = "APCS template probe (delete me)";

    internal const int MaximumColors = 20;

    /// <summary>Cameras tried per side: Printify shows a side from one to a few angles.</summary>
    internal const int MaximumCameras = 4;

    /// <summary>How far the measured print area's shape may be from Printify's before the admin is warned.</summary>
    internal const double AspectTolerance = 0.08;

    private static readonly TimeSpan JobLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan StoreLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SheetLifetime = TimeSpan.FromDays(1);
    private static readonly IReadOnlyList<string> PreferredSizes = ["M", "L", "S", "XL"];
    private static readonly IReadOnlyList<string> DefaultSizes = ["S", "M", "L", "XL", "2XL"];

    internal static string JobKey(Guid jobId) => $"printify-template-job:v1:{jobId:N}";

    /// <summary>The Printify print area a side of the garment is printed through.</summary>
    internal static string PositionOf(string view) => view switch
    {
        MockupRules.ViewLeft => "left_sleeve",
        MockupRules.ViewRight => "right_sleeve",
        _ => view,
    };

    /// <summary>
    /// The print area a provider prints a side through: the side's own name, else that name followed by how it is
    /// printed (Fulfill Engine names its areas front_dtf, back_dtf, left_sleeve_dtf…). Null when it does not print the side.
    /// </summary>
    internal static string? PositionIn(string view, IEnumerable<string> positions)
    {
        var plain = PositionOf(view);
        var known = positions.ToList();
        return known.FirstOrDefault(position => string.Equals(position, plain, StringComparison.OrdinalIgnoreCase))
            ?? known.FirstOrDefault(position => IsPrintedVariantOf(position, plain));
    }

    // Small areas named after a side (front_left_chest, back_neck), which are not the side itself.
    private static readonly HashSet<string> SmallAreas = ["neck", "chest", "pocket", "collar", "hood", "label", "tag", "yoke"];

    // The plain name, then "side" (Gildan 42000's front_side_dtf) and one word for how it is printed, each optional.
    private static bool IsPrintedVariantOf(string position, string plain)
    {
        if (position.Length <= plain.Length + 1 || !position.StartsWith(plain + "_", StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = position[(plain.Length + 1)..].ToLowerInvariant().Split('_');
        if (rest[0] == "side")
            rest = rest[1..];
        return rest.Length == 0
            || rest.Length == 1 && rest[0].Length > 0 && rest[0].All(char.IsLetterOrDigit) && !SmallAreas.Contains(rest[0]);
    }

    private static readonly IReadOnlyList<string> JobViews = [MockupRules.ViewFront, .. MockupRules.OtherViews];

    public async Task<Result<PrintifyTemplateJobDto>> StartAsync(StartPrintifyTemplateJobRequestDto request, CancellationToken cancellationToken = default)
    {
        var views = Normalize(request.Views);
        if (!views.Contains(MockupRules.ViewFront))
            return Result.Failure<PrintifyTemplateJobDto>(PrintifyTemplateErrors.FrontRequired());
        return await QueueAsync(request.BlueprintId, request.PrintProviderId, null, views, request.Colors, cancellationToken);
    }

    public async Task<Result<PrintifyTemplateJobDto>> StartForTemplateAsync(Guid templateId, StartPrintifyTemplateUpdateRequestDto request, CancellationToken cancellationToken = default)
    {
        var views = Normalize(request.Views);
        if (views.Contains(MockupRules.ViewFront))
            return Result.Failure<PrintifyTemplateJobDto>(PrintifyTemplateErrors.FrontIsTheTemplate());
        var link = await LinkedSystemTemplateAsync(templateId, cancellationToken);
        if (link is null)
            return Result.Failure<PrintifyTemplateJobDto>(PrintifyTemplateErrors.TemplateNotLinked());
        if (link.PrintProviderId is not long providerId)
            return Result.Failure<PrintifyTemplateJobDto>(PrintifyTemplateErrors.TemplateNotLinked());
        return await QueueAsync(link.BlueprintId, providerId, templateId, views, request.Colors, cancellationToken);
    }

    private async Task<Result<PrintifyTemplateJobDto>> QueueAsync(long blueprintId, long providerId, Guid? templateId,
        IReadOnlyList<string> views, IReadOnlyList<string>? colors, CancellationToken cancellationToken)
    {
        if (views.FirstOrDefault(view => !JobViews.Contains(view)) is { } unknown)
            return Result.Failure<PrintifyTemplateJobDto>(PrintifyTemplateErrors.UnknownView(unknown));
        if (adminToken.Token is not { } token)
            return Result.Failure<PrintifyTemplateJobDto>(PrintifyTemplateErrors.NoToken());
        var wanted = (colors ?? []).Select(color => color.Trim()).Where(color => color.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (views.Count == 0 && wanted.Count == 0)
            return Result.Failure<PrintifyTemplateJobDto>(PrintifyTemplateErrors.NothingToMeasure());
        if (wanted.Count > MaximumColors)
            return Result.Failure<PrintifyTemplateJobDto>(PrintifyTemplateErrors.TooManyColors(MaximumColors));

        PrintifyBlueprint? blueprint;
        PrintifyProvider? provider;
        IReadOnlyList<PrintifyCatalogVariant> variants;
        try
        {
            blueprint = (await catalog.BlueprintsAsync(token, cancellationToken)).FirstOrDefault(item => item.Id == blueprintId);
            provider = (await catalog.ProvidersAsync(token, blueprintId, cancellationToken)).FirstOrDefault(item => item.Id == providerId);
            variants = provider is null ? [] : await catalog.VariantsAsync(token, blueprintId, providerId, cancellationToken);
        }
        catch (PrintifyApiException exception)
        {
            return Result.Failure<PrintifyTemplateJobDto>(PrintifyAdminCatalog.Failure(exception));
        }
        if (blueprint is null || provider is null || variants.Count == 0)
            return Result.Failure<PrintifyTemplateJobDto>(Error.Validation("This provider does not make this product. Choose another provider."));

        var positions = variants.SelectMany(variant => variant.Placeholders).Select(area => area.Position).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (views.FirstOrDefault(view => PositionIn(view, positions) is null) is { } notPrinted)
            return Result.Failure<PrintifyTemplateJobDto>(PrintifyTemplateErrors.ViewNotPrinted(notPrinted, provider.Title));
        var made = variants.Select(variant => variant.Color).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var white = made.FirstOrDefault(color => color.Equals("White", StringComparison.OrdinalIgnoreCase));
        if (white is null)
            return Result.Failure<PrintifyTemplateJobDto>(PrintifyTemplateErrors.NoWhite());
        var named = new List<string> { white };
        foreach (var color in wanted)
        {
            var match = made.FirstOrDefault(item => item.Equals(color, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Result.Failure<PrintifyTemplateJobDto>(PrintifyTemplateErrors.ColorNotMade(color));
            if (!named.Contains(match))
                named.Add(match);
        }

        var job = new PrintifyTemplateJobDto(Guid.NewGuid(), Queued, "Waiting to start", null, blueprintId, blueprint.Title, blueprint.ImageUrl,
            providerId, provider.Title, templateId, views, named, [], [], [],
            variants.Select(variant => variant.Size).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            timeProvider.GetUtcNow());
        await cache.SetAsync(JobKey(job.JobId), job, JobLifetime, cancellationToken);
        queue.Enqueue(job.JobId);
        return Result.Success(job);
    }

    private static List<string> Normalize(IReadOnlyList<string>? views) =>
        (views ?? []).Select(view => view.Trim().ToLowerInvariant()).Where(view => view.Length > 0).Distinct().ToList();

    public async Task<Result<PrintifyTemplateJobDto>> GetAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        await cache.GetAsync<PrintifyTemplateJobDto>(JobKey(jobId), cancellationToken) is { } job
            ? Result.Success(job)
            : Result.Failure<PrintifyTemplateJobDto>(PrintifyTemplateErrors.JobNotFound());

    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await cache.GetAsync<PrintifyTemplateJobDto>(JobKey(jobId), cancellationToken);
        if (job is null || job.Status != Queued)
            return;

        var probes = new List<string>();
        long shopId = 0;
        var token = adminToken.Token;
        try
        {
            if (token is null)
                throw new JobFailure(PrintifyTemplateErrors.NoToken().Message);
            job = await StepAsync(job, "Finding the Printify store", cancellationToken);
            shopId = await StoreAsync(token, cancellationToken);
            job = await StepAsync(job, "Uploading the calibration sheets", cancellationToken);
            var sheets = await SheetsAsync(token, refresh: false, cancellationToken);

            var variants = await catalog.VariantsAsync(token, job.BlueprintId, job.PrintProviderId, cancellationToken);
            var chosen = job.Colors.Select(color => PickVariant(variants, color)).OfType<PrintifyCatalogVariant>().ToList();
            var white = chosen.FirstOrDefault(variant => string.Equals(variant.Color, "White", StringComparison.OrdinalIgnoreCase))
                ?? throw new JobFailure(PrintifyTemplateErrors.NoWhite().Message);
            var printed = variants.SelectMany(variant => variant.Placeholders).Select(area => area.Position).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string PositionOfView(string view) => PositionIn(view, printed) ?? PositionOf(view);
            var positions = job.Views.Select(PositionOfView).ToList();

            IReadOnlyList<PrintifyProductImage> sheetImages = [];
            if (positions.Count > 0)
            {
                job = await StepAsync(job, "Printing the calibration sheet on a temporary product", cancellationToken);
                var sheetProduct = await ProbeAsync(token, shopId, job, [white.Id], image: uploaded => uploaded.CalibrationId, positions, sheets, cancellationToken);
                probes.Add(sheetProduct.Id);
                sheetImages = await ImagesAsync(token, shopId, sheetProduct, cancellationToken);
            }
            job = await StepAsync(job, "Printing a bare garment on a temporary product", cancellationToken);
            var bareProduct = await ProbeAsync(token, shopId, job, chosen.Select(variant => variant.Id).ToList(), image: uploaded => uploaded.BlankId,
                positions.Count > 0 ? positions : [PositionOfView(MockupRules.ViewFront)], sheets, cancellationToken);
            probes.Add(bareProduct.Id);
            var bareImages = await ImagesAsync(token, shopId, bareProduct, cancellationToken);
            var colorOf = await ColorsOfImagesAsync(token, job.BlueprintId, bareImages, chosen, cancellationToken);

            job = await StepAsync(job, "Reading the mock-ups", cancellationToken);
            var downloads = new Dictionary<string, byte[]>();
            async Task<byte[]> DownloadAsync(string src)
            {
                if (!downloads.TryGetValue(src, out var bytes))
                    downloads[src] = bytes = await calibrator.DownloadMockupAsync(src, cancellationToken);
                return bytes;
            }
            PrintifyProductImage? BareOf(string camera, string color) =>
                bareImages.FirstOrDefault(image => CameraOf(image.Src) == camera && colorOf.TryGetValue(image.Src, out var own)
                    && string.Equals(own, color, StringComparison.OrdinalIgnoreCase));

            var warnings = new List<string>();
            var sides = new List<PrintifySideResultDto>();
            foreach (var view in job.Views)
            {
                var position = PositionOfView(view);
                var placeholder = white.Placeholders.FirstOrDefault(area => string.Equals(area.Position, position, StringComparison.OrdinalIgnoreCase));
                var cameras = sheetImages
                    .Select(image => (Camera: CameraOf(image.Src), Image: image))
                    .Where(item => ViewOfCamera(item.Camera, item.Image.Position) == view)
                    .DistinctBy(item => item.Camera)
                    .OrderBy(item => item.Camera == view ? 0 : 1)
                    .Take(MaximumCameras)
                    .ToList();
                var candidates = new List<PrintifyCameraCandidateDto>();
                string? problem = null;
                foreach (var (camera, sheetImage) in cameras)
                {
                    if (BareOf(camera, white.Color!) is not { } bare)
                        continue;
                    var detection = calibrator.DetectPrintArea(await DownloadAsync(sheetImage.Src), await DownloadAsync(bare.Src));
                    if (!detection.Found)
                    {
                        problem ??= detection.Problem;
                        continue;
                    }
                    var area = detection.Area!;
                    if (placeholder is { Width: > 0, Height: > 0 }
                        && AspectWarning(view, camera, area, placeholder) is { } warning)
                        warnings.Add(warning);
                    var stored = await StorePhotoAsync(job.JobId, view, camera, downloads[bare.Src], cancellationToken);
                    candidates.Add(new PrintifyCameraCandidateDto(camera, stored, detection.Width, detection.Height, area.X, area.Y, area.Width, area.Height));
                }
                sides.Add(new PrintifySideResultDto(view, position, candidates,
                    candidates.Count > 0 ? null : problem ?? "Printify shows no photo of this side."));
            }

            job = await StepAsync(job with { Sides = sides }, "Measuring colors", cancellationToken);
            var measured = new List<PrintifyMeasuredColorDto>();
            if (await ColorReferenceAsync(job, sides, cancellationToken) is { } reference
                && BareOf(reference.Camera, white.Color!) is { } whiteBare)
            {
                var whitePhoto = await DownloadAsync(whiteBare.Src);
                foreach (var variant in chosen)
                {
                    var color = variant.Color!;
                    if (BareOf(reference.Camera, color) is not { } colorImage)
                    {
                        warnings.Add($"Printify shows no photo of {color}, so it was not measured.");
                        continue;
                    }
                    var fabric = calibrator.MeasureFabric(await DownloadAsync(colorImage.Src), whitePhoto, reference.Area);
                    if (fabric is null)
                    {
                        warnings.Add($"{color} could not be measured on Printify's photo.");
                        continue;
                    }
                    measured.Add(new PrintifyMeasuredColorDto(color, fabric.PhotoHex,
                        string.Equals(color, white.Color, StringComparison.OrdinalIgnoreCase) ? "#FFFFFF" : fabric.RecolorHex));
                }
            }
            else if (chosen.Count > 1)
            {
                warnings.Add("The colors were not measured: no photo of the front was found.");
            }

            job = job with { Status = Done, Step = null, Sides = sides, MeasuredColors = measured, Warnings = warnings };
        }
        catch (JobFailure failure)
        {
            job = job with { Status = Failed, Step = null, Error = failure.Message };
        }
        catch (PrintifyApiException exception)
        {
            job = job with { Status = Failed, Step = null, Error = PrintifyAdminCatalog.Failure(exception).Message };
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Printify template job {JobId} failed at \"{Step}\".", jobId, job.Step);
            job = job with { Status = Failed, Step = null, Error = FailureMessage(job.Step, exception) };
        }
        finally
        {
            // Never left in the store, whatever happened above.
            foreach (var productId in probes)
                await DeleteProbeAsync(token!, shopId, productId);
        }
        await cache.SetAsync(JobKey(jobId), job, JobLifetime, CancellationToken.None);
    }

    /// <summary>
    /// What went wrong, for the admin who started the job: the step it was on and the error's own words, cut short.
    /// Only admins see a job, so the error is shown rather than hidden behind "try again".
    /// </summary>
    internal static string FailureMessage(string? step, Exception exception)
    {
        var reason = $"{exception.GetType().Name}: {exception.Message}";
        if (reason.Length > 300)
            reason = reason[..300];
        return step is null
            ? $"Measuring the Printify product failed ({reason})."
            : $"Measuring the Printify product failed while {char.ToLowerInvariant(step[0])}{step[1..]} ({reason}).";
    }

    private async Task<PrintifyTemplateJobDto> StepAsync(PrintifyTemplateJobDto job, string step, CancellationToken cancellationToken)
    {
        var next = job with { Status = Running, Step = step };
        await cache.SetAsync(JobKey(job.JobId), next, JobLifetime, cancellationToken);
        return next;
    }

    private async Task DeleteProbeAsync(string token, long shopId, string productId)
    {
        try
        {
            await printify.DeleteProductAsync(token, shopId, productId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The temporary Printify product {ProductId} could not be deleted; delete \"{Title}\" in the store by hand.",
                productId, ProbeTitle);
        }
    }

    private sealed class JobFailure(string message) : Exception(message);

    internal sealed record ProbeSheets(string CalibrationId, string BlankId);

    private static string TokenKey(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..16];

    // The first store of the account: the products made there are deleted again within minutes.
    private async Task<long> StoreAsync(string token, CancellationToken cancellationToken)
    {
        var key = $"printify:admin:store:{TokenKey(token)}";
        if (await cache.GetAsync<PrintifyShop>(key, cancellationToken) is { } cached)
            return cached.Id;
        var store = (await printify.GetShopsAsync(token, cancellationToken)).OrderBy(shop => shop.Id).FirstOrDefault()
            ?? throw new JobFailure(PrintifyTemplateErrors.NoStore().Message);
        await cache.SetAsync(key, store, StoreLifetime, cancellationToken);
        return store.Id;
    }

    // The two sheets are uploaded to the account once and reused.
    private async Task<ProbeSheets> SheetsAsync(string token, bool refresh, CancellationToken cancellationToken)
    {
        var key = $"printify:admin:sheets:{TokenKey(token)}";
        if (!refresh && await cache.GetAsync<ProbeSheets>(key, cancellationToken) is { } cached)
            return cached;
        var (calibration, blank) = calibrator.Sheets();
        var sheets = new ProbeSheets(
            (await printify.UploadImageContentsAsync(token, "apcs-printify-calibration.png", calibration, cancellationToken)).Id,
            (await printify.UploadImageContentsAsync(token, "apcs-printify-blank.png", blank, cancellationToken)).Id);
        await cache.SetAsync(key, sheets, SheetLifetime, cancellationToken);
        return sheets;
    }

    // A sheet removed from the account since it was uploaded is uploaded again once.
    private async Task<PrintifyProduct> ProbeAsync(string token, long shopId, PrintifyTemplateJobDto job, IReadOnlyList<long> variantIds,
        Func<ProbeSheets, string> image, IReadOnlyList<string> positions, ProbeSheets sheets, CancellationToken cancellationToken)
    {
        PrintifyProbeProduct Probe(ProbeSheets current) => new(ProbeTitle, job.BlueprintId, job.PrintProviderId, variantIds, image(current), positions);
        try
        {
            return await printify.CreateProbeProductAsync(token, shopId, Probe(sheets), cancellationToken);
        }
        catch (PrintifyApiException exception) when (exception.Kind == PrintifyFailureKind.Rejected)
        {
            return await printify.CreateProbeProductAsync(token, shopId, Probe(await SheetsAsync(token, refresh: true, cancellationToken)), cancellationToken);
        }
    }

    // Printify may take a moment to make a new product's mock-ups.
    private async Task<IReadOnlyList<PrintifyProductImage>> ImagesAsync(string token, long shopId, PrintifyProduct product, CancellationToken cancellationToken)
    {
        if (product.Images is { Count: > 0 } ready)
            return ready;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(4), timeProvider, cancellationToken);
            var current = await printify.GetProductAsync(token, shopId, product.Id, cancellationToken);
            if (current.Images is { Count: > 0 } images)
                return images;
        }
        throw new JobFailure("Printify made no mock-ups of the temporary product. Try again in a minute.");
    }

    internal static PrintifyCatalogVariant? PickVariant(IReadOnlyList<PrintifyCatalogVariant> variants, string color)
    {
        var ofColor = variants.Where(variant => string.Equals(variant.Color, color, StringComparison.OrdinalIgnoreCase)).ToList();
        return PreferredSizes.Select(size => ofColor.FirstOrDefault(variant => string.Equals(variant.Size, size, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(variant => variant is not null) ?? ofColor.FirstOrDefault();
    }

    /// <summary>
    /// The garment color each mock-up photo shows. Printify lists the product's variants a photo is of; failing that,
    /// its URL names a variant, which may be another provider's of the same color, or else is nearest a variant made.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> ColorsOfImagesAsync(string token, long blueprintId,
        IReadOnlyList<PrintifyProductImage> productImages, IReadOnlyList<PrintifyCatalogVariant> made, CancellationToken cancellationToken)
    {
        var own = made.Where(variant => variant.Color is not null).ToDictionary(variant => variant.Id, variant => variant.Color!);
        var colors = new Dictionary<string, string>();
        var unknown = new List<(string Src, long Id)>();
        foreach (var image in productImages)
        {
            if (image.VariantIds.Select(id => own.GetValueOrDefault(id)).FirstOrDefault(color => color is not null) is { } listed)
                colors[image.Src] = listed;
            else if (VariantIdOf(image.Src) is long id)
            {
                if (own.TryGetValue(id, out var named))
                    colors[image.Src] = named;
                else
                    unknown.Add((image.Src, id));
            }
        }
        if (unknown.Count == 0 || own.Count == 0)
            return colors;

        var everyProvider = await catalog.ColorsOfAllProvidersAsync(token, blueprintId, cancellationToken);
        foreach (var (src, id) in unknown)
            colors[src] = everyProvider.TryGetValue(id, out var color) ? color : own.MinBy(pair => Math.Abs(pair.Key - id)).Value;
        return colors;
    }

    // https://images-api.printify.com/mockup/{product}/{variant}/{camera}/{file}?camera_label=front
    internal static long? VariantIdOf(string src) =>
        Uri.TryCreate(src, UriKind.Absolute, out var uri) && uri.Segments.Length > 3
        && long.TryParse(uri.Segments[3].Trim('/'), out var id) ? id : null;

    /// <summary>The camera a mock-up photo is taken from: its <c>camera_label</c>, else the camera id in its URL.</summary>
    internal static string CameraOf(string src)
    {
        if (!Uri.TryCreate(src, UriKind.Absolute, out var uri))
            return string.Empty;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair[0] == "camera_label" && pair.Length == 2 && pair[1].Length > 0)
                return Uri.UnescapeDataString(pair[1]).ToLowerInvariant();
        }
        return uri.Segments.Length > 4 ? uri.Segments[4].Trim('/') : string.Empty;
    }

    /// <summary>The side of the garment a camera faces, from its label, else the print area Printify says it shows.</summary>
    internal static string? ViewOfCamera(string camera, string? position)
    {
        if (camera.StartsWith(MockupRules.ViewFront, StringComparison.Ordinal)) return MockupRules.ViewFront;
        if (camera.StartsWith(MockupRules.ViewBack, StringComparison.Ordinal)) return MockupRules.ViewBack;
        if (camera.Contains(MockupRules.ViewLeft, StringComparison.Ordinal)) return MockupRules.ViewLeft;
        if (camera.Contains(MockupRules.ViewRight, StringComparison.Ordinal)) return MockupRules.ViewRight;
        return position is null ? null : JobViews.FirstOrDefault(view => PositionIn(view, [position]) is not null);
    }

    /// <summary>A warning when the print area found is not the shape Printify prints: most likely another provider's photo.</summary>
    internal static string? AspectWarning(string view, string camera, PrintAreaOnPhoto area, PrintifyPlaceholder placeholder)
    {
        var measured = (double)area.Width / area.Height;
        var printed = (double)placeholder.Width / placeholder.Height;
        return Math.Abs(measured / printed - 1) <= AspectTolerance
            ? null
            : $"The {view} print area on the \"{camera}\" photo is {measured:0.00}:1, but Printify prints {printed:0.00}:1. Check the provider before using it.";
    }

    // Colors are measured where the front print area is: on the job's front photo, or the linked template's own.
    private async Task<(string Camera, PrintAreaOnPhoto Area)?> ColorReferenceAsync(PrintifyTemplateJobDto job,
        IReadOnlyList<PrintifySideResultDto> sides, CancellationToken cancellationToken)
    {
        if (sides.FirstOrDefault(side => side.View == MockupRules.ViewFront)?.Candidates.FirstOrDefault() is { } front)
            return (front.Camera, new PrintAreaOnPhoto(front.X, front.Y, front.Width, front.Height));
        if (job.TemplateId is not Guid templateId
            || await templates.GetByIdAsync(templateId, cancellationToken) is not { } template
            || MockupRules.ParsePrintLayout(template.PrintAreaConfig)?.Main is not { } main)
            return null;
        return (MockupRules.ViewFront, new PrintAreaOnPhoto(main.X, main.Y, main.Width, main.Height));
    }

    private static string ProbePhotoKey(Guid jobId, string view, string camera) =>
        $"mockup-templates/printify-probes/{jobId:N}/{view}-{new string(camera.Where(char.IsLetterOrDigit).ToArray())}";

    // The bare photo is kept beyond the throwaway product, to show the admin and to become the template's photo.
    private async Task<string> StorePhotoAsync(Guid jobId, string view, string camera, byte[] photo, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(photo);
        var stored = await images.UploadImageWithMetadataAsync(
            new UploadFileDto($"{view}-{camera}.jpg", "image/jpeg", stream.Length, stream), ProbePhotoKey(jobId, view, camera), cancellationToken);
        return stored.Url;
    }

    public async Task<Result<MockupTemplateResponseDto>> CreateAsync(Guid jobId, CreatePrintifyTemplateRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid adminId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.Unauthenticated("create"));
        if (await cache.GetAsync<PrintifyTemplateJobDto>(JobKey(jobId), cancellationToken) is not { } job)
            return Result.Failure<MockupTemplateResponseDto>(PrintifyTemplateErrors.JobNotFound());
        if (job.Status != Done)
            return Result.Failure<MockupTemplateResponseDto>(PrintifyTemplateErrors.JobNotDone());

        var chosen = new Dictionary<string, PrintifyCameraCandidateDto>();
        foreach (var (rawView, camera) in request.Cameras ?? new Dictionary<string, string>())
        {
            var view = rawView.Trim().ToLowerInvariant();
            var candidate = job.Sides.FirstOrDefault(side => side.View == view)?.Candidates.FirstOrDefault(item => item.Camera == camera);
            if (candidate is null)
                return Result.Failure<MockupTemplateResponseDto>(PrintifyTemplateErrors.CameraNotMeasured(view));
            chosen[view] = candidate;
        }
        var keptColors = new List<PrintifyMeasuredColorDto>();
        foreach (var colorName in request.Colors ?? [])
        {
            var color = job.MeasuredColors.FirstOrDefault(item => string.Equals(item.Name, colorName, StringComparison.OrdinalIgnoreCase));
            if (color is null)
                return Result.Failure<MockupTemplateResponseDto>(PrintifyTemplateErrors.ColorNotMade(colorName));
            keptColors.Add(color);
        }

        MockupTemplate? existing = null;
        if (job.TemplateId is Guid templateId)
        {
            if (await LinkedSystemTemplateAsync(templateId, cancellationToken) is null)
                return Result.Failure<MockupTemplateResponseDto>(PrintifyTemplateErrors.TemplateNotLinked());
            existing = await templates.GetByIdAsync(templateId, cancellationToken);
            if (existing is null)
                return Result.Failure<MockupTemplateResponseDto>(PrintifyTemplateErrors.TemplateNotLinked());
        }
        else
        {
            if (!chosen.ContainsKey(MockupRules.ViewFront))
                return Result.Failure<MockupTemplateResponseDto>(PrintifyTemplateErrors.CameraNotMeasured(MockupRules.ViewFront));
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ProductType))
                return Result.Failure<MockupTemplateResponseDto>(PrintifyTemplateErrors.NameRequired());
        }

        var name = existing?.Name ?? request.Name!.Trim();
        var productType = existing?.ProductType ?? request.ProductType!.Trim();
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        Guid mainId;
        if (existing is null)
        {
            var front = await SaveSideAsync(name, productType, MockupRules.ViewFront, chosen[MockupRules.ViewFront], null, cancellationToken);
            if (front.IsFailure)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return front;
            }
            mainId = front.Value.Id;
        }
        else
        {
            mainId = existing.Id;
        }

        foreach (var view in MockupRules.OtherViews.Where(chosen.ContainsKey))
        {
            var side = await SaveSideAsync($"{name} - {char.ToUpperInvariant(view[0])}{view[1..]}", productType, view, chosen[view], mainId, cancellationToken);
            if (side.IsFailure)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return side;
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (existing is null)
        {
            var sizes = (request.DefaultSizes is { Count: > 0 } asked ? asked : DefaultSizes)
                .Where(size => job.Sizes.Contains(size, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            await links.AddAsync(new MockupTemplatePrintifyLink
            {
                MockupTemplateId = mainId,
                BlueprintId = job.BlueprintId,
                BlueprintTitle = job.BlueprintTitle,
                BlueprintImageUrl = job.BlueprintImageUrl,
                PrintProviderId = job.PrintProviderId,
                PrintProviderTitle = job.PrintProviderTitle,
                DefaultSizes = sizes.Count > 0 ? sizes : job.Sizes.Take(1).ToList(),
                UpdatedBy = adminId,
                CreatedAt = now,
                UpdatedAt = now,
            }, saveChange: false, cancellationToken);
        }
        foreach (var color in keptColors)
        {
            var saved = await colorMap.UpsertAsync(job.BlueprintId, color.Name, color.RecolorHex, cancellationToken);
            if (saved.IsFailure)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return Result.Failure<MockupTemplateResponseDto>(saved.Error);
            }
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await cache.RemoveAsync(JobKey(jobId), CancellationToken.None);
        foreach (var candidate in job.Sides.SelectMany(side => side.Candidates.Select(item => (side.View, item.Camera))))
        {
            try
            {
                await images.DeleteImageAsync(ProbePhotoKey(jobId, candidate.View, candidate.Camera), CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogInformation(exception, "A measured Printify photo of job {JobId} could not be removed.", jobId);
            }
        }
        return await SystemTemplateAsync(mainId, cancellationToken);
    }

    // One side saved as the template service saves any photo (helper images, recolor check), as a shared template.
    private async Task<Result<MockupTemplateResponseDto>> SaveSideAsync(string name, string productType, string view,
        PrintifyCameraCandidateDto candidate, Guid? mainId, CancellationToken cancellationToken)
    {
        byte[] photo;
        try
        {
            photo = await mapService.DownloadAsync(candidate.PhotoUrl, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The measured Printify photo {Url} could not be read back.", candidate.PhotoUrl);
            return Result.Failure<MockupTemplateResponseDto>(PrintifyTemplateErrors.JobNotFound());
        }
        using var stream = new MemoryStream(photo);
        return await templateService.CreateAsync(new CreateMockupTemplateRequestDto(
            name, productType, candidate.X, candidate.Y, candidate.Width, candidate.Height,
            new UploadFileDto($"{view}.jpg", "image/jpeg", stream.Length, stream),
            AllowRecolor: true, View: view, MainTemplateId: mainId, FromPrintify: true), system: true, cancellationToken);
    }

    // The Printify link of an active shared template, or null.
    private async Task<MockupTemplatePrintifyLink?> LinkedSystemTemplateAsync(Guid templateId, CancellationToken cancellationToken) =>
        await links.Query()
            .Where(link => link.MockupTemplateId == templateId && link.MockupTemplate.UserId == null && link.MockupTemplate.IsActive == true)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<Result<MockupTemplateResponseDto>> SystemTemplateAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var listed = await templateService.ListSystemAsync(cancellationToken);
        if (listed.IsFailure)
            return Result.Failure<MockupTemplateResponseDto>(listed.Error);
        return listed.Value.FirstOrDefault(template => template.Id == templateId) is { } found
            ? Result.Success(found)
            : Result.Failure<MockupTemplateResponseDto>(MockupErrors.TemplateNotFoundSingle());
    }
}
