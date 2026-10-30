using APCS.Api.Controllers;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Common;
using APCS.Application.Features.Rag.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class RagControllerTests
{
    [TestMethod]
    public async Task Ingest_VideoExperience_ReturnsBadRequestWithoutCallingGenericRagService()
    {
        var service = new Mock<IRagService>(MockBehavior.Strict);
        var request = new RagIngestRequest("Lesson", "video-experience", null, "1", "en", "Content",
            ContentType: RagContentTypes.Video);

        var action = await new RagController(service.Object).Ingest(request, CancellationToken.None);

        action.Should().BeOfType<BadRequestObjectResult>();
        service.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Search_VideoExperienceScope_ReturnsBadRequestWithoutCallingGenericRagService()
    {
        var service = new Mock<IRagService>(MockBehavior.Strict);
        var request = new RagSearchRequest("hands", ContentType: RagContentTypes.Video,
            VideoExperience: new(Guid.NewGuid(), "lifestyle_in_use", "minimax_h3", "touch collar"));

        var action = await new RagController(service.Object).Search(request, CancellationToken.None);

        action.Should().BeOfType<BadRequestObjectResult>();
        service.VerifyNoOtherCalls();
    }
}
