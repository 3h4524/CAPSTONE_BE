namespace APCS.Application.Features.DesignGeneration.Common;

/// <summary>
/// The values of <c>design_images.approval_status</c>; the DB constraint <c>chk_design_images_approval</c> allows exactly these.
/// </summary>
public static class ApprovalStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static readonly string[] All = [Pending, Approved, Rejected];
}
