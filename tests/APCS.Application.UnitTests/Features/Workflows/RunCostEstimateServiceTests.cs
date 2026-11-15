using APCS.Common.Constants;
using System.Text.Json;
using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class RunCostEstimateServiceTests
{
    private static readonly Guid Owner = WorkflowTestData.Owner;
    private readonly List<Workflow> _workflows = [];
    private readonly List<Product> _products = [];
    private readonly List<DesignImage> _designs = [];
    private readonly Guid _batch = Guid.NewGuid();

    private RunCostEstimateService Service()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(Owner);
        var gemini = new Mock<IImageGenerationProvider>();
        gemini.SetupGet(x => x.Provider).Returns(ImageGenerationProviders.Gemini);
        gemini.SetupGet(x => x.EstimatedCostPerImageUsd).Returns(0.05m);
        var ideogram = new Mock<IImageGenerationProvider>();
        ideogram.SetupGet(x => x.Provider).Returns(ImageGenerationProviders.Ideogram);
        ideogram.SetupGet(x => x.EstimatedCostPerImageUsd).Returns(0.04m);
        return new(user.Object, WorkflowTestData.Repository(_workflows, x => x.Id).Object, WorkflowTestData.Repository(_products, x => x.Id).Object,
            WorkflowTestData.Repository(_designs, x => x.Id).Object, [gemini.Object, ideogram.Object]);
    }

    private Product AddProduct(string name)
    {
        var product = new Product { Id = Guid.NewGuid(), UserId = Owner, BatchId = _batch, Name = name, ProductType = "tshirt" };
        _products.Add(product);
        return product;
    }

    private static AiVideoSceneOptions Scene(string mode, string template = "cinematic_product_showcase", int seconds = 6, string? model = null) =>
        new(TemplateCode: template, DurationSeconds: seconds, Mode: mode, AiVideoModelKey: model);

    /// <summary>A workflow designing <paramref name="variants"/> variants per product, then a video of these scenes.</summary>
    private Workflow AddWorkflow(int variants, params AiVideoSceneOptions[] scenes) =>
        AddWorkflow(variants, new GenerateVideoConfig(Mode: "ai_shot", AiScenes: scenes));

    private Workflow AddWorkflow(int variants, GenerateVideoConfig video)
    {
        WorkflowNode Node(string type, object config) => new(type, type, type, new(0, 0), JsonSerializer.SerializeToElement(config, WorkflowJson.Options));
        var definition = new WorkflowDefinition(2,
        [
            Node("product-input", new ProductInputConfig(_batch, Guid.Empty)),
            Node("design-image", new { imageProvider = "gemini", variants }),
            Node("generate-video", video),
        ], []);
        var workflow = new Workflow { Id = Guid.NewGuid(), UserId = Owner, Name = "w", Definition = WorkflowJson.Write(definition) };
        _workflows.Add(workflow);
        return workflow;
    }

    [TestMethod]
    public async Task EstimateAsync_ListsDesignImagesAndEachProductsAiClips()
    {
        AddProduct("Tee A");
        AddProduct("Tee B");
        // 6 s on Premium (0.08) + 4 s on Economy (0.04) per video; the Standard scene costs nothing.
        var workflow = AddWorkflow(2, Scene("ai"), Scene("ai", seconds: 4, model: AiVideoModel.LiteModelKey), Scene("standard"));

        var estimate = (await Service().EstimateAsync(workflow.Id, default)).Value;

        estimate.Items.Single(x => x.Kind == "design_images").Should().BeEquivalentTo(new { Quantity = 4, EstimatedCostUsd = 0.20m });
        var videos = estimate.Items.Where(x => x.Kind == "ai_video").ToArray();
        videos.Select(x => x.Label).Should().Equal("Tee A", "Tee B");
        videos.Should().OnlyContain(x => x.Quantity == 4 && x.EstimatedCostUsd == 2 * 0.64m);
        estimate.AiVideoBudgetUsd.Should().Be(2.56m);
        estimate.TotalUsd.Should().Be(2.76m);
        estimate.Items.Should().NotContain(x => x.Kind == "model_photos");
    }

    [TestMethod]
    public async Task EstimateAsync_ScenesWithAPerson_CountAnIdeogramModelPhotoPerDesign()
    {
        AddProduct("Tee A");
        var workflow = AddWorkflow(2, Scene("ai", template: "lifestyle_in_use"), Scene("ai", template: "lifestyle_in_use"));

        var estimate = (await Service().EstimateAsync(workflow.Id, default)).Value;

        // One per design, however many scenes show the person.
        estimate.Items.Single(x => x.Kind == "model_photos").Should().BeEquivalentTo(new { Quantity = 2, EstimatedCostUsd = 2 * 0.04m });
    }

    [TestMethod]
    public async Task EstimateAsync_ProductDesignedBefore_KeepsItsApprovedDesignAndIsNotDesignedAgain()
    {
        var designed = AddProduct("Tee A");
        _designs.Add(new DesignImage { Id = Guid.NewGuid(), ProductId = designed.Id, ApprovalStatus = "approved" });
        var workflow = AddWorkflow(3, Scene("ai", seconds: 5));

        var estimate = (await Service().EstimateAsync(workflow.Id, default)).Value;

        estimate.Items.Should().NotContain(x => x.Kind == "design_images");
        estimate.Items.Single(x => x.Kind == "ai_video").Should().BeEquivalentTo(new { Quantity = 1, EstimatedCostUsd = 0.40m });
    }

    [TestMethod]
    public async Task EstimateAsync_ProductWithItsOwnSettings_IsPricedFromThem()
    {
        AddProduct("Tee A");
        var onModel = AddProduct("Tee B");
        var shared = new GenerateVideoConfig(Mode: "standard", AiScenes: [Scene("standard")]);
        var own = new GenerateVideoConfig(Mode: "ai_shot", AiScenes: [Scene("ai", template: "lifestyle_in_use", seconds: 5)]);
        var workflow = AddWorkflow(1, shared with { ProductConfigs = new Dictionary<Guid, GenerateVideoConfig> { [onModel.Id] = own } });

        var estimate = (await Service().EstimateAsync(workflow.Id, default)).Value;

        estimate.Items.Where(x => x.Kind == "ai_video").Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Label = "Tee B", Quantity = 1, EstimatedCostUsd = 0.40m });
        estimate.Items.Single(x => x.Kind == "model_photos").Quantity.Should().Be(1);
    }

    // A Run with design steps makes a job of the batch's pending products only (BatchService.ApproveAsync): a product an
    // earlier Run finished is not designed or filmed again, so it is not priced, whatever its own settings are.
    [TestMethod]
    public async Task EstimateAsync_ProductsFinishedByAnEarlierRun_AreNotPricedAgain()
    {
        var davies = AddProduct("DAVIES T-Shirt");
        var ontop = AddProduct("ONTOP");
        var added = AddProduct("New tee");
        davies.ProcessingStatus = "completed";
        ontop.ProcessingStatus = "completed";
        added.ProcessingStatus = BatchJobProductStatuses.Pending;
        _designs.Add(new DesignImage { Id = Guid.NewGuid(), ProductId = davies.Id, ApprovalStatus = "approved" });
        _designs.Add(new DesignImage { Id = Guid.NewGuid(), ProductId = ontop.Id, ApprovalStatus = "approved" });
        var shared = new GenerateVideoConfig(Mode: "standard", AiScenes: [Scene("standard")]);
        var own = new GenerateVideoConfig(Mode: "ai_shot", AiScenes: [Scene("ai", seconds: 4)]);
        var workflow = AddWorkflow(1, shared with { ProductConfigs = new Dictionary<Guid, GenerateVideoConfig> { [ontop.Id] = own } });

        var estimate = (await Service().EstimateAsync(workflow.Id, default)).Value;

        estimate.Items.Single(x => x.Kind == "design_images").Quantity.Should().Be(1);
        estimate.Items.Should().NotContain(x => x.Label == "ONTOP" || x.Label == "DAVIES T-Shirt");
    }

    [TestMethod]
    public async Task EstimateAsync_NothingPending_PricesEveryProductTheRunGoesOnWith()
    {
        var ontop = AddProduct("ONTOP");
        ontop.ProcessingStatus = "completed";
        _designs.Add(new DesignImage { Id = Guid.NewGuid(), ProductId = ontop.Id, ApprovalStatus = "approved" });
        var workflow = AddWorkflow(1, Scene("ai", seconds: 4));

        var estimate = (await Service().EstimateAsync(workflow.Id, default)).Value;

        estimate.Items.Should().ContainSingle(x => x.Kind == "ai_video" && x.Label == "ONTOP");
    }

    [TestMethod]
    public void ForProduct_TheProductsOwnSettingsElseTheSharedOnes_NeverTheOtherProducts()
    {
        var product = Guid.NewGuid();
        var own = new GenerateVideoConfig(DurationSeconds: 5);
        var shared = new GenerateVideoConfig(DurationSeconds: 9, ProductConfigs: new Dictionary<Guid, GenerateVideoConfig> { [product] = own });

        shared.ForProduct(product).Should().Be(own);
        shared.ForProduct(Guid.NewGuid()).Should().Be(shared with { ProductConfigs = null });
        WorkflowJson.Write(shared.ForProduct(product)).Should().NotContain("productConfigs");
    }

    [TestMethod]
    public async Task EstimateAsync_AnotherSellersWorkflow_IsNotFound()
    {
        var workflow = AddWorkflow(1, Scene("ai"));
        workflow.UserId = Guid.NewGuid();

        (await Service().EstimateAsync(workflow.Id, default)).Error.Code.Should().Be("WorkflowNotFound");
    }
}

[TestClass]
public sealed class PersonModelPhotosTests
{
    private static MockupImage Mockup(string role = "Hero", string status = "approved", string source = MockupSources.Generated, int order = 0) => new()
    {
        Id = Guid.NewGuid(), Role = role, ApprovalStatus = status, SourceType = source, DesignImageId = Guid.NewGuid(),
        MockupTemplateId = Guid.NewGuid(), Regions = "{}", CreatedAt = new DateTime(2026, 11, 11).AddMinutes(order)
    };

    private static MockupTemplate Model(string name, string productType = "tshirt", string? people = null, Guid? owner = null) => new()
    {
        Id = Guid.NewGuid(), Name = name, ProductType = productType, Kind = MockupRules.ModelKind, IsActive = true, AllowRecolor = true,
        People = people, UserId = owner, BaseImageUrl = "u", PrintMapsSourceUrl = "u", PrintMapsVersion = 1, PrintAreaConfig = "{}"
    };

    [TestMethod]
    public void Needs_OnlyAnAiSceneThatShowsAPerson()
    {
        PersonModelPhotos.Needs(new(AiScenes: [new(TemplateCode: "lifestyle_in_use", Mode: "ai")])).Should().BeTrue();
        PersonModelPhotos.Needs(new(AiScenes: [new(TemplateCode: "lifestyle_in_use", Mode: "standard")])).Should().BeFalse();
        PersonModelPhotos.Needs(new(AiScenes: [new(TemplateCode: "cinematic_product_showcase", Mode: "ai")])).Should().BeFalse();
        PersonModelPhotos.Needs(new()).Should().BeFalse();
    }

    [TestMethod]
    public void Has_CountsAModelPhotoWaitingOrApprovedButNotARejectedOne()
    {
        PersonModelPhotos.Has([Mockup(), Mockup(ModelPhotoMockupService.Role, "pending")]).Should().BeTrue();
        PersonModelPhotos.Has([Mockup(), Mockup(ModelPhotoMockupService.Role, "rejected")]).Should().BeFalse();
    }

    [TestMethod]
    public void Garments_TheHeroFromATemplateFirst_NeverAModelPhotoOrARejectedOne()
    {
        var angle = Mockup("AlternativeAngle");
        var pendingHero = Mockup(status: "pending", order: 1);
        var uploadedHero = Mockup(source: MockupSources.Uploaded, order: 2);
        uploadedHero.MockupTemplateId = null;
        var rejected = Mockup(status: "rejected");
        var photo = Mockup(ModelPhotoMockupService.Role);

        PersonModelPhotos.Garments([angle, uploadedHero, pendingHero, rejected, photo])
            .Should().Equal(pendingHero, uploadedHero, angle);
    }

    [TestMethod]
    public void DefaultModel_TheLibrarysSinglePersonPhotoForTheProductType_ByName()
    {
        var group = Model("Aaron", people: ModelPeople.Group);
        var mine = Model("Abe", owner: Guid.NewGuid());
        var hoodie = Model("Adam", productType: "hoodie");
        var zoe = Model("Zoe");
        var mia = Model("Mia");

        PersonModelPhotos.DefaultModel([group, mine, hoodie, zoe, mia], "TSHIRT").Should().Be(mia);
        PersonModelPhotos.DefaultModel([hoodie], "tshirt").Should().BeNull();
    }
}
