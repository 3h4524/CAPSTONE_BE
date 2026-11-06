using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Tools.ClientActions;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class NavigateAndChoicesToolTests
{
    private static AssistantToolContext Context(AssistantCaller caller) => new(Guid.NewGuid(), AssistantTestSupport.UserId, caller, null);

    [TestMethod]
    [DataRow("/usage")]
    [DataRow("/workflows")]
    [DataRow("/workflows?id=11111111-1111-1111-1111-111111111111")]
    [DataRow("/workflows?id=11111111-1111-1111-1111-111111111111&run=22222222-2222-2222-2222-222222222222")]
    public async Task Navigate_SellerAllowedPath_EmitsClientAction(string path)
    {
        var outcome = await new NavigateTool().ExecuteAsync(Context(AssistantTestSupport.Seller), AssistantTestSupport.Json($$"""{"path":"{{path}}","label":"Open"}"""), default);

        outcome.Succeeded.Should().BeTrue();
        outcome.EndsTurn.Should().BeTrue();
        outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Which.Kind.Should().Be("navigate");
    }

    [TestMethod]
    [DataRow("/admin/users")]
    [DataRow("https://evil.example/")]
    [DataRow("//evil.example")]
    [DataRow("/workflows?id=not-a-guid")]
    [DataRow("/unknown")]
    [DataRow("")]
    public async Task Navigate_SellerDisallowedPath_IsRejected(string path)
    {
        var outcome = await new NavigateTool().ExecuteAsync(Context(AssistantTestSupport.Seller), AssistantTestSupport.Json($$"""{"path":"{{path}}","label":"Open"}"""), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("route_not_allowed");
        outcome.Events.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Navigate_AdminAllowedOnlyAdminPaths()
    {
        var tool = new NavigateTool();

        (await tool.ExecuteAsync(Context(AssistantTestSupport.Admin), AssistantTestSupport.Json("""{"path":"/admin/users","label":"Users"}"""), default)).Succeeded.Should().BeTrue();
        (await tool.ExecuteAsync(Context(AssistantTestSupport.Admin), AssistantTestSupport.Json("""{"path":"/workflows","label":"Flows"}"""), default)).Succeeded.Should().BeFalse();
    }

    [TestMethod]
    public async Task Navigate_LabelLongerThanLimit_IsRejected()
    {
        var outcome = await new NavigateTool().ExecuteAsync(Context(AssistantTestSupport.Seller), AssistantTestSupport.Json($$"""{"path":"/usage","label":"{{new string('l', 41)}}"}"""), default);

        outcome.ModelResultJson.Should().Contain("invalid_arguments");
    }

    [TestMethod]
    public async Task OfferChoices_WithValidOptions_EmitsChoicesAndDefaultsFreeTextToTrue()
    {
        var outcome = await new OfferChoicesTool().ExecuteAsync(Context(AssistantTestSupport.Seller),
            AssistantTestSupport.Json("""{"question":"Which?","options":[{"label":"Standard","value":"standard"},{"label":"AI","value":"ai"}]}"""), default);

        outcome.EndsTurn.Should().BeTrue();
        var choices = outcome.Events.Single().Data.Should().BeOfType<AssistantChoicesData>().Subject;
        choices.AllowFreeText.Should().BeTrue();
        choices.Options.Should().HaveCount(2);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(6)]
    public async Task OfferChoices_WithWrongOptionCount_IsRejected(int count)
    {
        var options = string.Join(",", Enumerable.Range(0, count).Select(i => $$"""{"label":"L{{i}}","value":"v{{i}}"}"""));

        var outcome = await new OfferChoicesTool().ExecuteAsync(Context(AssistantTestSupport.Seller), AssistantTestSupport.Json($$"""{"options":[{{options}}]}"""), default);

        outcome.Succeeded.Should().BeFalse();
    }

    [TestMethod]
    public async Task OfferChoices_LabelLongerThanLimit_IsRejected()
    {
        var args = $$"""{"options":[{"label":"{{new string('a', 41)}}","value":"v"},{"label":"b","value":"w"}]}""";

        var outcome = await new OfferChoicesTool().ExecuteAsync(Context(AssistantTestSupport.Seller), AssistantTestSupport.Json(args), default);

        outcome.Succeeded.Should().BeFalse();
    }

    [TestMethod]
    public async Task Navigate_WithReply_UsesItAsTheFinalText()
    {
        var outcome = await new NavigateTool().ExecuteAsync(Context(AssistantTestSupport.Seller), AssistantTestSupport.Json("""{"path":"/usage","label":"Open","reply":"Here is your usage page."}"""), default);

        outcome.FinalText.Should().Be("Here is your usage page.");
    }

    [TestMethod]
    public async Task Navigate_WithoutReply_LeavesTheFinalAnswerToTheModel()
    {
        var outcome = await new NavigateTool().ExecuteAsync(Context(AssistantTestSupport.Seller), AssistantTestSupport.Json("""{"path":"/usage","label":"Open"}"""), default);

        outcome.FinalText.Should().BeNull();
    }

    [TestMethod]
    public async Task OfferChoices_WithQuestion_NeedsNoExtraText()
    {
        var outcome = await new OfferChoicesTool().ExecuteAsync(Context(AssistantTestSupport.Seller),
            AssistantTestSupport.Json("""{"question":"Which?","options":[{"label":"A","value":"a"},{"label":"B","value":"b"}]}"""), default);

        outcome.FinalText.Should().BeEmpty();
    }

    [TestMethod]
    public async Task OfferChoices_WithoutQuestion_LeavesTheTextToTheModel()
    {
        var outcome = await new OfferChoicesTool().ExecuteAsync(Context(AssistantTestSupport.Seller),
            AssistantTestSupport.Json("""{"options":[{"label":"A","value":"a"},{"label":"B","value":"b"}]}"""), default);

        outcome.FinalText.Should().BeNull();
    }
}
