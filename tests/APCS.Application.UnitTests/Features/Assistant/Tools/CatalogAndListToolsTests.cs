using System.Text.Json;
using APCS.Application.Features.Assistant.Catalog;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Tools.Query;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Application.Features.DesignTemplates;
using APCS.Application.Features.DesignTemplates.Dtos.Request;
using APCS.Application.Features.DesignTemplates.Dtos.Response;
using APCS.Application.Features.StyleArtPresets;
using APCS.Application.Features.StyleArtPresets.Dtos.Response;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class CatalogAndListToolsTests
{
    private static AssistantToolContext Context(bool seller = true) =>
        new(Guid.NewGuid(), AssistantTestSupport.UserId, seller ? AssistantTestSupport.Seller : AssistantTestSupport.Admin, null);

    [TestMethod]
    public void Catalog_RunOrderMatchesTheWorkflowCapabilityRegistry()
    {
        WorkflowNodeCatalog.RunOrder.Should().Equal(WorkflowCapabilityRegistry.RunOrder);
    }

    [TestMethod]
    public void Catalog_EveryEnabledNodeTypeIsDescribed()
    {
        var enabled = new WorkflowCapabilityRegistry().Get().Nodes.Where(node => node.Enabled).Select(node => node.Type);

        WorkflowNodeCatalog.Nodes.Select(node => node.Type).Should().BeEquivalentTo(enabled);
    }

    [TestMethod]
    public async Task GetNodeCatalog_WithoutTypes_ReturnsOverviewWithRulesAndOptionValues()
    {
        var outcome = await new GetNodeCatalogTool().ExecuteAsync(Context(), default, default);

        var root = JsonDocument.Parse(outcome.ModelResultJson).RootElement;
        root.GetProperty("connectionRules").GetArrayLength().Should().BeGreaterThan(3);
        root.GetProperty("nodes").GetArrayLength().Should().Be(WorkflowNodeCatalog.Nodes.Count);
        outcome.ModelResultJson.Should().Contain("ideogram").And.NotContain("Nano Banana");
    }

    [TestMethod]
    public async Task GetNodeCatalog_WithTypes_ReturnsFullDetailForThoseOnly()
    {
        var outcome = await new GetNodeCatalogTool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"types":["design-image"]}"""), default);

        var nodes = JsonDocument.Parse(outcome.ModelResultJson).RootElement.GetProperty("nodes");
        nodes.GetArrayLength().Should().Be(1);
        outcome.ModelResultJson.Should().Contain("Nano Banana");
    }

    [TestMethod]
    public async Task GetNodeCatalog_WithUnknownType_ListsValidTypes()
    {
        var outcome = await new GetNodeCatalogTool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"types":["nope"]}"""), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("unknown_type").And.Contain("generate-listing");
    }

    [TestMethod]
    public void NewTools_AreSellerOnly()
    {
        var tools = new IAssistantTool[]
        {
            new GetNodeCatalogTool(),
            new ListDesignTemplatesTool(new Mock<IDesignTemplateService>().Object),
            new ListStylePresetsTool(new Mock<IStyleArtPresetService>().Object),
            new ListMockupTemplatesTool(new Mock<IMockupTemplateService>().Object),
            new ListVideoTemplatesTool(new Mock<IVideoPlanningService>().Object)
        };

        tools.Select(tool => tool.IsAvailable(Context(seller: false))).Should().AllBeEquivalentTo(false);
        tools.Select(tool => tool.IsAvailable(Context())).Should().AllBeEquivalentTo(true);
    }

    [TestMethod]
    public async Task ListDesignTemplates_DefaultsToSystemScopeAndMapsItems()
    {
        var service = new Mock<IDesignTemplateService>();
        ListDesignTemplatesRequestDto? captured = null;
        var item = new DesignTemplateSummaryResponseDto(Guid.NewGuid(), "Retro", "pets", "vintage", null, null, true, 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, false, false, false);
        service.Setup(s => s.ListAsync(It.IsAny<ListDesignTemplatesRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<ListDesignTemplatesRequestDto, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Result.Success(new PagedResult<DesignTemplateSummaryResponseDto>([item], 1, 1, 20)));

        var outcome = await new ListDesignTemplatesTool(service.Object).ExecuteAsync(Context(), AssistantTestSupport.Json("""{"scope":"bogus"}"""), default);

        captured!.Scope.Should().Be("system");
        outcome.ModelResultJson.Should().Contain("Retro").And.Contain("vintage");
    }

    [TestMethod]
    public async Task ListStylePresets_ReturnsClippedDescriptions()
    {
        var service = new Mock<IStyleArtPresetService>();
        var preset = new StyleArtPresetResponseDto(Guid.NewGuid(), "Neon", new string('d', 400), "mods", null, [], true, false, 0);
        service.Setup(s => s.ListMineAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success<IReadOnlyList<StyleArtPresetResponseDto>>([preset]));

        var outcome = await new ListStylePresetsTool(service.Object).ExecuteAsync(Context(), default, default);

        outcome.ModelResultJson.Should().Contain("Neon").And.NotContain(new string('d', 200));
    }

    [TestMethod]
    public async Task ListMockupTemplates_PassesTheProductTypeFilter()
    {
        var service = new Mock<IMockupTemplateService>();
        service.Setup(s => s.ListAsync("mug", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success<IReadOnlyList<MockupTemplateResponseDto>>([]));

        var outcome = await new ListMockupTemplatesTool(service.Object).ExecuteAsync(Context(), AssistantTestSupport.Json("""{"productType":"mug"}"""), default);

        outcome.Succeeded.Should().BeTrue();
        service.Verify(s => s.ListAsync("mug", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ListVideoTemplates_MapsTheCatalog()
    {
        var service = new Mock<IVideoPlanningService>();
        var item = new VideoTemplateCatalogItem("product_showcase", 2, "Product Showcase", "desc", null, 12, "fade", "zoom", new VideoTemplateRequirements(1, false, 1));
        service.Setup(s => s.ListTemplatesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success<IReadOnlyList<VideoTemplateCatalogItem>>([item]));

        var outcome = await new ListVideoTemplatesTool(service.Object).ExecuteAsync(Context(), default, default);

        outcome.ModelResultJson.Should().Contain("product_showcase").And.Contain("\"defaultTransition\":\"fade\"");
    }
}
