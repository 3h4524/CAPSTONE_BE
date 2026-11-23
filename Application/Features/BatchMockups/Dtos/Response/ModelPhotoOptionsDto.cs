namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>What the app offers for model photos.</summary>
/// <param name="PrintWithoutAi">Whether a mock-up can be put on a model photo without generative AI.</param>
public sealed record ModelPhotoOptionsDto(bool PrintWithoutAi);
