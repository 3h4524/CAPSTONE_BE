namespace APCS.Application.Features.Revenues.Dtos;

/// <summary>
/// A plan's paying customers at the start of a period, split by where they were at its end, the
/// same split the Revenue page's usage signal uses.
/// </summary>
/// <param name="Stayed">Still on the plan at the end.</param>
/// <param name="Left">Paying for nothing at the end. Customers who moved to another plan are in neither list.</param>
public sealed record PlanCohortDto(string PlanName, IReadOnlyList<Guid> Stayed, IReadOnlyList<LeftCustomerDto> Left);

/// <summary>A customer who stopped paying.</summary>
/// <param name="FirstPaidOn">The day they first paid for any plan.</param>
/// <param name="LeftOn">The first day they no longer paid for anything.</param>
public sealed record LeftCustomerDto(Guid UserId, DateOnly FirstPaidOn, DateOnly LeftOn);
