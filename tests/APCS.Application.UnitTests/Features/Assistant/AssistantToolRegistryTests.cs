using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Tools;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Assistant;

[TestClass]
public sealed class AssistantToolRegistryTests
{
    private static AssistantToolContext Context(AssistantCaller caller, AssistantClientContextDto? client = null) =>
        new(Guid.NewGuid(), AssistantTestSupport.UserId, caller, client);

    [TestMethod]
    public void Resolve_ReturnsOnlyAvailableTools()
    {
        var registry = new AssistantToolRegistry([
            new StubTool("always"),
            new StubTool("seller_only", available: context => context.Caller.IsSeller),
            new StubTool("canvas", available: context => context.Client?.Canvas is not null)]);

        registry.Resolve(Context(AssistantTestSupport.Seller)).Select(tool => tool.Name).Should().Equal("always", "seller_only");
        registry.Resolve(Context(AssistantTestSupport.Admin)).Select(tool => tool.Name).Should().Equal("always");
    }

    [TestMethod]
    public void Resolve_WithCanvas_IncludesCanvasTool()
    {
        var canvas = new AssistantCanvasDto(Guid.NewGuid(), 1, false, [], []);
        var client = new AssistantClientContextDto("/workflows", null, null, null, canvas);
        var registry = new AssistantToolRegistry([new StubTool("canvas", available: context => context.Client?.Canvas is not null)]);

        registry.Resolve(Context(AssistantTestSupport.Seller, client)).Should().ContainSingle();
    }

    [TestMethod]
    public void FindConfirmable_ReturnsOnlyConfirmationTools()
    {
        var registry = new AssistantToolRegistry([new StubTool("query")]);

        registry.FindConfirmable("query").Should().BeNull();
    }
}
