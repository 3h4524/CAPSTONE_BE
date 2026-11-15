using APCS.Api.Contracts.BatchMockups;
using APCS.Api.Extensions;
using APCS.Application.Features.Admin.MockupTemplates;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Application.Features.BatchMockups.Printify;
using APCS.Application.Features.Shops.Dtos.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// The shared mock-up templates every seller can use, managed by admins: ordinary ones made from an uploaded photo,
/// and ones of Printify products. A Printify template is made from Printify's own photos (measured there), or an
/// uploaded one is linked to the Printify product it shows by hand.
/// </summary>
[ApiController]
[Route("api/admin/mockup-templates")]
[Authorize(Roles = "Admin,Super Admin")]
public sealed class AdminMockupTemplatesController(
    IMockupTemplateService service,
    IAdminMockupTemplateService links,
    IPrintifyAdminCatalog catalog,
    IPrintifyTemplateBuilder builder,
    IPrintifyColorMapService colors) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MockupTemplateResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await service.ListSystemAsync(cancellationToken)).ToActionResult(this);

    /// <summary>Every system template (inactive ones and model photos too) with its Printify link, as linking by hand lists them.</summary>
    [HttpGet("printify-links")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminMockupTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PrintifyLinks(CancellationToken cancellationToken) =>
        (await links.ListAsync(cancellationToken)).ToActionResult(this);

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Create([FromForm] CreateMockupTemplateForm form, CancellationToken cancellationToken) =>
        MockupTemplateEndpoints.CreateAsync(this, form, request => service.CreateAsync(request, system: true, cancellationToken));

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Update(Guid id, [FromForm] UpdateMockupTemplateForm form, CancellationToken cancellationToken) =>
        MockupTemplateEndpoints.UpdateAsync(this, form, request => service.UpdateAsync(id, request, system: true, cancellationToken));

    [HttpPost("garment-mask-preview")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(GarmentMaskPreviewResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> PreviewGarmentMask(IFormFile? baseImage, CancellationToken cancellationToken) =>
        MockupTemplateEndpoints.PreviewAsync(this, baseImage, photo => service.PreviewGarmentMaskAsync(photo, system: true, cancellationToken));

    [HttpPut("{id:guid}/views/{view}")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetView(Guid id, string view, [FromBody] SetMockupTemplateViewRequest request, CancellationToken cancellationToken) =>
        (await service.SetViewAsync(id, view, request.TemplateId, system: true, cancellationToken)).ToActionResult(this);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, system: true, cancellationToken)).ToActionResult(this);

    // ---- Printify catalog, read with the server's own token ----

    [HttpGet("printify/blueprints")]
    [ProducesResponseType(typeof(IReadOnlyList<PrintifyBlueprintDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Blueprints([FromQuery] string? q, CancellationToken cancellationToken) =>
        (await catalog.SearchBlueprintsAsync(q, cancellationToken)).ToActionResult(this);

    [HttpGet("printify/blueprints/{blueprintId:long}/providers")]
    [ProducesResponseType(typeof(IReadOnlyList<PrintifyProviderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Providers(long blueprintId, CancellationToken cancellationToken) =>
        (await catalog.GetProvidersAsync(blueprintId, cancellationToken)).ToActionResult(this);

    [HttpGet("printify/blueprints/{blueprintId:long}/providers/{providerId:long}")]
    [ProducesResponseType(typeof(PrintifyCatalogDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Catalog(long blueprintId, long providerId, CancellationToken cancellationToken) =>
        (await catalog.GetCatalogAsync(blueprintId, providerId, cancellationToken)).ToActionResult(this);

    // ---- Templates of Printify products ----

    /// <summary>Starts measuring a Printify product for a new template; poll the job until it is done.</summary>
    [HttpPost("printify/jobs")]
    [ProducesResponseType(typeof(PrintifyTemplateJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartJob([FromBody] StartPrintifyTemplateJobRequestDto request, CancellationToken cancellationToken) =>
        (await builder.StartAsync(request, cancellationToken)).ToActionResult(this);

    [HttpGet("printify/jobs/{jobId:guid}")]
    [ProducesResponseType(typeof(PrintifyTemplateJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJob(Guid jobId, CancellationToken cancellationToken) =>
        (await builder.GetAsync(jobId, cancellationToken)).ToActionResult(this);

    /// <summary>Saves the template, or a linked template's new sides and colors, from a finished job.</summary>
    [HttpPost("printify/jobs/{jobId:guid}/create")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateFromJob(Guid jobId, [FromBody] CreatePrintifyTemplateRequestDto request, CancellationToken cancellationToken) =>
        (await builder.CreateAsync(jobId, request, cancellationToken)).ToActionResult(this);

    /// <summary>Starts measuring more sides or colors of a template linked to Printify.</summary>
    [HttpPost("{id:guid}/printify/jobs")]
    [ProducesResponseType(typeof(PrintifyTemplateJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartTemplateJob(Guid id, [FromBody] StartPrintifyTemplateUpdateRequestDto request, CancellationToken cancellationToken) =>
        (await builder.StartForTemplateAsync(id, request, cancellationToken)).ToActionResult(this);

    /// <summary>
    /// Links a shared template to the Printify product its photo shows, or changes the link (provider, default sizes).
    /// A template made from Printify is linked already; this links an uploaded one by hand.
    /// </summary>
    [HttpPut("{id:guid}/printify")]
    [ProducesResponseType(typeof(AdminMockupTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LinkPrintify(Guid id, [FromBody] LinkMockupTemplatePrintifyRequestDto request, CancellationToken cancellationToken) =>
        (await links.LinkPrintifyAsync(id, request, cancellationToken)).ToActionResult(this);

    /// <summary>Unlinks the template from Printify; it stays, with its photos, as an ordinary template.</summary>
    [HttpDelete("{id:guid}/printify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlinkPrintify(Guid id, CancellationToken cancellationToken) =>
        (await links.UnlinkPrintifyAsync(id, cancellationToken)).ToActionResult(this);

    // ---- Garment colors of each Printify blueprint ----

    [HttpGet("printify/colors")]
    [ProducesResponseType(typeof(IReadOnlyList<PrintifyColorRowDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Colors([FromQuery] long blueprintId, CancellationToken cancellationToken) =>
        (await colors.ListAsync(blueprintId, cancellationToken)).ToActionResult(this);

    [HttpPut("printify/colors")]
    [ProducesResponseType(typeof(PrintifyColorRowDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveColor([FromBody] SavePrintifyColorRequestDto request, CancellationToken cancellationToken) =>
        (await colors.SaveAsync(request, cancellationToken)).ToActionResult(this);

    [HttpDelete("printify/colors/{colorId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteColor(Guid colorId, CancellationToken cancellationToken) =>
        (await colors.DeleteAsync(colorId, cancellationToken)).ToActionResult(this);
}
