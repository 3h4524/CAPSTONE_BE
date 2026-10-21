namespace APCS.Application.Features.Rag.Common;

public static class RagDocumentStatuses
{
    public const string Processing = "processing";
    public const string Ready = "ready";
    public const string Superseded = "superseded";
    public const string Failed = "failed";

    public static bool IsSupported(string value) => value is Processing or Ready or Superseded or Failed;
}
