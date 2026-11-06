using System.Text.RegularExpressions;

namespace APCS.Application.Features.Assistant.Tools.ClientActions;

public static partial class AssistantNavigationRoutes
{
    private static readonly HashSet<string> SellerPaths =
    [
        "/dashboard", "/workflows", "/batches", "/styles", "/design-templates", "/api-keys",
        "/usage", "/market-opportunities", "/support", "/subscription", "/profile"
    ];

    private static readonly HashSet<string> AdminPaths =
    [
        "/admin/dashboard", "/admin/revenue", "/admin/analytics", "/admin/support-tickets",
        "/admin/subscription-plans", "/admin/rag-ingest", "/admin/users", "/admin/test-reports"
    ];

    public static bool IsAllowed(string? path, bool isSeller, bool isAdmin)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (isSeller && (SellerPaths.Contains(path) || WorkflowRoute().IsMatch(path)))
            return true;
        return isAdmin && AdminPaths.Contains(path);
    }

    [GeneratedRegex(@"^/workflows\?id=[0-9a-fA-F-]{36}(&run=[0-9a-fA-F-]{36})?$")]
    private static partial Regex WorkflowRoute();
}
