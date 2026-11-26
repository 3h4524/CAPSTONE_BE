using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.BatchMockups;

[TestClass]
public sealed class ModelPhotoCategoryTests
{
    [TestMethod]
    [DataRow("tshirt", "hoodie")]
    [DataRow("hoodie", "tshirt")]
    [DataRow("HOODIE", "tshirt")]
    [DataRow("tshirt", "tshirt")]
    public void ModelFits_TopsShareApparelModels(string model, string product) =>
        MockupRules.ModelFits(model, product).Should().BeTrue();

    [TestMethod]
    [DataRow("tshirt", "tote_bag")]
    [DataRow("tote_bag", "hoodie")]
    public void ModelFits_OtherProductsKeepTheirOwnType(string model, string product) =>
        MockupRules.ModelFits(model, product).Should().BeFalse();

    [TestMethod]
    public void ModelTypesFor_ATop_IsEveryApparelType()
    {
        MockupRules.ModelTypesFor("Hoodie").Should().BeEquivalentTo(["tshirt", "hoodie"]);
        MockupRules.ModelTypesFor("tote_bag").Should().Equal("tote_bag");
    }

    [TestMethod]
    public void ModelTypesFor_TheApparelGroupItself_IsEveryApparelType() =>
        MockupRules.ModelTypesFor("apparel").Should().BeEquivalentTo(["tshirt", "hoodie"]);

    [TestMethod]
    public void ChosenPhoto_UnderApparel_IsUsedForTshirtsAndHoodies()
    {
        var photo = Guid.NewGuid();
        var config = new MockupModelPhotoConfig(new Dictionary<string, Guid> { ["apparel"] = photo });

        config.For("tshirt").Should().Be(photo);
        config.For("HOODIE").Should().Be(photo);
        config.For("tote_bag").Should().BeNull();
    }

    [TestMethod]
    public void ChosenPhoto_SavedUnderATypeBefore_IsStillReadForTheOtherTop_AfterTheSharedOne()
    {
        var tshirt = Guid.NewGuid();
        var shared = Guid.NewGuid();

        new MockupModelPhotoConfig(new Dictionary<string, Guid> { ["tshirt"] = tshirt }).For("hoodie").Should().Be(tshirt);
        new MockupModelPhotoConfig(new Dictionary<string, Guid> { ["tshirt"] = tshirt, ["apparel"] = shared })
            .For("tshirt").Should().Be(shared);
    }

    [TestMethod]
    public void ChosenPhoto_ForAToteBag_StaysItsOwn()
    {
        var bag = Guid.NewGuid();
        var config = new MockupModelPhotoConfig(new Dictionary<string, Guid> { ["tote_bag"] = bag });

        config.For("tote_bag").Should().Be(bag);
        config.For("tshirt").Should().BeNull();
    }

    [TestMethod]
    public void DefaultModel_HoodieWithOnlyTshirtModels_TakesATshirtModel()
    {
        var tshirt = Model("tshirt", "Adult · Male");

        PersonModelPhotos.DefaultModel([tshirt], "hoodie").Should().BeSameAs(tshirt);
    }

    [TestMethod]
    public void DefaultModel_PrefersAModelOfTheProductsOwnType()
    {
        var tshirt = Model("tshirt", "A");
        var hoodie = Model("hoodie", "Z");

        PersonModelPhotos.DefaultModel([tshirt, hoodie], "hoodie").Should().BeSameAs(hoodie);
        PersonModelPhotos.DefaultModel([tshirt, hoodie], "tshirt").Should().BeSameAs(tshirt);
    }

    [TestMethod]
    public void DefaultModel_ToteBagWithOnlyApparelModels_HasNone() =>
        PersonModelPhotos.DefaultModel([Model("tshirt", "A"), Model("hoodie", "B")], "tote_bag").Should().BeNull();

    private static MockupTemplate Model(string productType, string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ProductType = productType,
        Kind = MockupRules.ModelKind,
        IsActive = true,
        People = ModelPeople.Single,
    };
}
