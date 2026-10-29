using System;

namespace APCS.Application.Features.Revenues.Dtos;

public class RevenueFilterDto
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Granularity { get; set; } = "Day"; // Day, Month, Year
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    // Transaction list only; the other revenue endpoints ignore these.
    /// <summary>Matches invoice number, customer name, customer email or user id, in full or in part (case-insensitive).</summary>
    public string? Search { get; set; }
    /// <summary>Exact plan name; empty for all plans.</summary>
    public string? Plan { get; set; }
    /// <summary>paid, pending, overdue, refunded, cancelled or draft; empty for all.</summary>
    public string? Status { get; set; }
}
