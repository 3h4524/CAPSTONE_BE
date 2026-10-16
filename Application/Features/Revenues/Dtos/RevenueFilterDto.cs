using System;

namespace APCS.Application.Features.Revenues.Dtos;

public class RevenueFilterDto
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Granularity { get; set; } = "Day"; // Day, Month, Year
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
