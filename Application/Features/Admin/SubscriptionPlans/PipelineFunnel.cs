namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>Turns the products that reached each workflow step into a funnel that never grows.</summary>
internal static class PipelineFunnel
{
    /// <summary>
    /// How many products reached each step and every step before it: a product with a mockup but no
    /// approved design stops at "designed", so each count is at most the one before.
    /// </summary>
    /// <param name="steps">The products that reached each step on their own, in workflow order.</param>
    public static IReadOnlyList<int> Nest(IReadOnlyList<IReadOnlySet<Guid>> steps)
    {
        var counts = new List<int>(steps.Count);
        HashSet<Guid>? reached = null;
        foreach (var step in steps)
        {
            reached = reached is null ? [.. step] : [.. reached.Where(step.Contains)];
            counts.Add(reached.Count);
        }

        return counts;
    }
}
