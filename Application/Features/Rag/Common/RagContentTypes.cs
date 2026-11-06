namespace APCS.Application.Features.Rag.Common;

public static class RagContentTypes
{
    public const string Listing = "listing";
    public const string Image = "image";
    public const string Video = "video";
    public const string Policy = "policy";
    public const string Keyword = "keyword";
    public const string Faq = "faq";

    public static bool IsSupported(string value) => value is Listing or Image or Video or Policy or Keyword or Faq;
}
