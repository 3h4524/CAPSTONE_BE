namespace APCS.Application.Features.Rag.Common;

/// <summary>Business lifecycle for a video lesson; distinct from document ingestion status.</summary>
public static class RagExperienceStatuses
{
    public const string Candidate = "candidate";
    public const string Verified = "verified";
    public const string Rejected = "rejected";
    public const string Retired = "retired";

    public static bool IsSupported(string value) => value is Candidate or Verified or Rejected or Retired;
}
