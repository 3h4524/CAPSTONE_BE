using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using APCS.Api.Contracts.BatchMockups;
using APCS.Api.Extensions;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>A seller's model photos: people wearing a plain, light garment that mock-ups are put on.</summary>
[ApiController]
[Route("api/model-photos")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class ModelPhotosController(IModelPhotoService service, IModelPhotoGenerationService generation,
    ILogger<ModelPhotosController>? logger = null) : ControllerBase
{
    /// <summary>What the app offers for model photos.</summary>
    [HttpGet("options")]
    [ProducesResponseType(typeof(ModelPhotoOptionsDto), StatusCodes.Status200OK)]
    public IActionResult Options() => Ok(service.GetOptions());

    /// <summary>The model photos the seller can use: the shared library and their own, narrowed to who is in them.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MockupTemplateResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? productType, [FromQuery] ModelPhotoQuery query, CancellationToken cancellationToken) =>
        (await service.ListAsync(productType, query.ToFilter(), cancellationToken)).ToActionResult(this);

    /// <summary>Makes a model photo with AI (the seller's Ideogram key) and keeps it as one of the seller's own.</summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Generate([FromBody] GenerateModelPhotoRequestDto request, CancellationToken cancellationToken) =>
        (await generation.GenerateAsync(request, system: false, cancellationToken)).ToActionResult(this);

    /// <summary>The same as a stream of events: the photo as soon as Ideogram has drawn it, then the saved model photo.</summary>
    [HttpPost("generate/stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult GenerateStream([FromBody] GenerateModelPhotoRequestDto request) =>
        ModelPhotoEndpoints.GenerationStream(generation, request, system: false, logger);

    /// <summary>Finds the person on a photo that is not saved yet, for selecting the garment on it.</summary>
    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(GarmentMaskPreviewResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Preview(IFormFile? photo, CancellationToken cancellationToken) =>
        ModelPhotoEndpoints.PreviewAsync(this, service, photo, cancellationToken);

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Create([FromForm] SaveModelPhotoForm form, CancellationToken cancellationToken) =>
        ModelPhotoEndpoints.SaveAsync(this, form, request => service.CreateAsync(request, system: false, cancellationToken));

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Update(Guid id, [FromForm] SaveModelPhotoForm form, CancellationToken cancellationToken) =>
        ModelPhotoEndpoints.SaveAsync(this, form, request => service.UpdateAsync(id, request, system: false, cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, system: false, cancellationToken)).ToActionResult(this);
}

/// <summary>The shared library of model photos every seller can use, managed by admins.</summary>
[ApiController]
[Route("api/admin/model-photos")]
[Authorize(Roles = "Admin,Super Admin")]
public sealed class AdminModelPhotosController(IModelPhotoService service, IModelPhotoGenerationService generation,
    ILogger<AdminModelPhotosController>? logger = null) : ControllerBase
{
    /// <summary>What the app offers for model photos.</summary>
    [HttpGet("options")]
    [ProducesResponseType(typeof(ModelPhotoOptionsDto), StatusCodes.Status200OK)]
    public IActionResult Options() => Ok(service.GetOptions());

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MockupTemplateResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ModelPhotoQuery query, CancellationToken cancellationToken) =>
        (await service.ListSystemAsync(query.ToFilter(), cancellationToken)).ToActionResult(this);

    /// <summary>Makes a model photo with AI (the admin's Ideogram key) for the shared library.</summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Generate([FromBody] GenerateModelPhotoRequestDto request, CancellationToken cancellationToken) =>
        (await generation.GenerateAsync(request, system: true, cancellationToken)).ToActionResult(this);

    /// <summary>The same as a stream of events: the photo as soon as Ideogram has drawn it, then the saved model photo.</summary>
    [HttpPost("generate/stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult GenerateStream([FromBody] GenerateModelPhotoRequestDto request) =>
        ModelPhotoEndpoints.GenerationStream(generation, request, system: true, logger);

    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(GarmentMaskPreviewResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Preview(IFormFile? photo, CancellationToken cancellationToken) =>
        ModelPhotoEndpoints.PreviewAsync(this, service, photo, cancellationToken);

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Create([FromForm] SaveModelPhotoForm form, CancellationToken cancellationToken) =>
        ModelPhotoEndpoints.SaveAsync(this, form, request => service.CreateAsync(request, system: true, cancellationToken));

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Update(Guid id, [FromForm] SaveModelPhotoForm form, CancellationToken cancellationToken) =>
        ModelPhotoEndpoints.SaveAsync(this, form, request => service.UpdateAsync(id, request, system: true, cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, system: true, cancellationToken)).ToActionResult(this);
}

/// <summary>Who the listed model photos show: each part narrows the list, and an empty one matches any.</summary>
public sealed class ModelPhotoQuery
{
    public string? People { get; set; }
    public string? AgeGroup { get; set; }
    public string? Gender { get; set; }
    public string? GroupType { get; set; }

    public ModelPhotoFilter ToFilter() => new(People, AgeGroup, Gender, GroupType);
}

internal static class ModelPhotoEndpoints
{
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

    public static IActionResult GenerationStream(IModelPhotoGenerationService generation, GenerateModelPhotoRequestDto request, bool system, ILogger? logger) =>
        new ServerSentEventsResult(GenerationEventsAsync(generation, request, system, logger));

    // started, then preview (the link to the drawn photo), then saved or failed. The request stays open until the work is
    // done even if the person leaves, because the services it uses live as long as the request.
    private static async IAsyncEnumerable<EventStreamEntry> GenerationEventsAsync(IModelPhotoGenerationService generation,
        GenerateModelPhotoRequestDto request, bool system, ILogger? logger, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var number = 0;
        var events = Channel.CreateUnbounded<EventStreamEntry>();
        EventStreamEntry Entry(string type, object data) => new($"{++number}-0", type, JsonSerializer.Serialize(data, EventJson));

        async Task RunAsync()
        {
            try
            {
                var result = await generation.GenerateAsync(request, system, link =>
                {
                    events.Writer.TryWrite(Entry("preview", new { url = link }));
                    return Task.CompletedTask;
                }, cancellationToken);
                events.Writer.TryWrite(result.IsSuccess
                    ? Entry("saved", result.Value)
                    : Entry("failed", new { code = result.Error.Code, message = result.Error.Message }));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The person left before the photo was drawn: nobody is listening.
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "A model photo could not be made");
                events.Writer.TryWrite(Entry("failed", new { code = "ModelPhotoGenerationFailed", message = "The photo could not be made. Try again." }));
            }
            finally
            {
                events.Writer.TryComplete();
            }
        }

        yield return Entry("started", new { });
        var work = RunAsync();
        try
        {
            await foreach (var entry in events.Reader.ReadAllAsync(cancellationToken))
                yield return entry;
        }
        finally
        {
            await work;
        }
    }

    public static async Task<IActionResult> PreviewAsync(ControllerBase controller, IModelPhotoService service, IFormFile? photo, CancellationToken cancellationToken)
    {
        var upload = photo.ToUploadFileDto();
        try
        {
            return (await service.PreviewAsync(upload, cancellationToken)).ToActionResult(controller);
        }
        finally
        {
            upload?.Content.Dispose();
        }
    }

    public static async Task<IActionResult> SaveAsync(ControllerBase controller, SaveModelPhotoForm form,
        Func<SaveModelPhotoRequestDto, Task<APCS.Common.Models.Result<MockupTemplateResponseDto>>> save)
    {
        var request = form.ToRequest();
        try
        {
            return (await save(request)).ToActionResult(controller);
        }
        finally
        {
            request.Photo?.Content.Dispose();
            request.GarmentSelection?.Content.Dispose();
        }
    }
}
