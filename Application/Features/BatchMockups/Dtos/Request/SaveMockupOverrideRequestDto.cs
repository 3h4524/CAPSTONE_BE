using APCS.Application.Features.BatchMockups.Dtos.Response;

namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>Makes one mock-up of a batch job its own way, apart from the job's shared settings.</summary>
/// <param name="SlotKey">Which mock-up: the slot key the job's mock-up results give it.</param>
/// <param name="Override">How that mock-up is made from now on.</param>
/// <param name="ReplacedMockupImageId">
/// The mock-up shown for it until now. It is removed once it is no longer one of the job's mock-ups, unless it was
/// already added to the video sources: there it may be approved or in use, so the seller decides about it.
/// </param>
public sealed record SaveMockupOverrideRequestDto(
    string SlotKey,
    MockupOverrideDto Override,
    Guid? ReplacedMockupImageId = null);
