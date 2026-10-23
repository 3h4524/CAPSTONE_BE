namespace APCS.Application.Features.DesignGeneration.Dtos.Request;

/// <summary>Generates one product of a finished job again, after the Seller's changes to what it is made from.</summary>
/// <param name="CustomPrompt">The product's own image prompt: null leaves it as it is, empty removes it.</param>
/// <param name="DesignText">The words printed on the design: null leaves them as they are, empty removes them.</param>
public sealed record RegenerateProductRequestDto(string? CustomPrompt = null, string? DesignText = null);
