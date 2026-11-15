namespace APCS.Api.Contracts.BatchMockups;

/// <summary>The template whose photo shows a side of another template; null takes the side off.</summary>
public sealed record SetMockupTemplateViewRequest(Guid? TemplateId);
