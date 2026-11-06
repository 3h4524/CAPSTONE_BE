using APCS.Application.Features.BatchMockups.Common;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.BatchMockups;

[TestClass]
public sealed class ModelAttributesTests
{
    [TestMethod]
    public void Problem_OnePersonNeedsAnAgeGroupAndAGender_AndNoGroupType()
    {
        ModelPeople.Problem(new("single", "child", "female")).Should().BeNull();
        ModelPeople.Problem(new("Single", " Senior ", "MALE")).Should().BeNull();
        ModelPeople.Problem(new("single", null, "female")).Should().Contain("age group");
        ModelPeople.Problem(new("single", "toddler", "female")).Should().Contain("age group");
        ModelPeople.Problem(new("single", "adult", null)).Should().Contain("gender");
        ModelPeople.Problem(new("single", "adult", "male", "family")).Should().Contain("no group type");
    }

    [TestMethod]
    public void Problem_AGroupNeedsAGroupType_AndNoAgeGroupOrGender()
    {
        ModelPeople.Problem(new("group", GroupType: "couple")).Should().BeNull();
        ModelPeople.Problem(new("group")).Should().Contain("family, friends or a couple");
        ModelPeople.Problem(new("group", GroupType: "team")).Should().Contain("family, friends or a couple");
        ModelPeople.Problem(new("group", "adult", null, "friends")).Should().Contain("no single age group");
        ModelPeople.Problem(new("crowd")).Should().Contain("one person or several");
    }

    [TestMethod]
    public void Apply_KeepsTheAttributesLowerCasedAndClearsWhatDoesNotApply()
    {
        var template = new MockupTemplate { AgeGroup = "adult", Gender = "male" };

        ModelPeople.Apply(template, new("Group", GroupType: "Family"));

        template.People.Should().Be("group");
        template.AgeGroup.Should().BeNull();
        template.Gender.Should().BeNull();
        template.GroupType.Should().Be("family");
        ModelPeople.Of(template).Should().Be(new ModelAttributesDto("group", null, null, "family"));
    }

    [TestMethod]
    public void Label_NamesThePersonOrTheGroup()
    {
        ModelPeople.Label(new("single", "teen", "female")).Should().Be("Teen · Female");
        ModelPeople.Label(new("group", GroupType: "friends")).Should().Be("Friends");
    }

    [TestMethod]
    public void Prompts_OnePerson_AreAPlainWhiteTeeOnAGreyBackdrop_InPortrait()
    {
        var (prompt, ratio) = ModelPhotoPrompts.Build(new("single", "child", "female"));

        ratio.Should().Be("3:4");
        prompt.Should().Contain("a girl about 8 years old")
            .And.Contain("plain white crew-neck short-sleeve cotton t-shirt with no print")
            .And.Contain("medium grey seamless backdrop")
            .And.Contain("facing the camera").And.Contain("nothing covers the chest");
    }

    [TestMethod]
    public void Prompts_AGroup_StandApartInLandscape()
    {
        var (prompt, ratio) = ModelPhotoPrompts.Build(new("group", GroupType: "family"));

        ratio.Should().Be("4:3");
        prompt.Should().Contain("a family of exactly three people").And.Contain("Everyone wears the same plain white")
            .And.Contain("t-shirts do not touch or overlap");
    }

    [TestMethod]
    public void Prompts_ACouple_SayThereAreExactlyTwoPeople()
    {
        var (prompt, _) = ModelPhotoPrompts.Build(new("group", GroupType: "couple"));

        prompt.Should().Contain("exactly two people").And.Contain("one woman and one man").And.Contain("candid couple photo")
            .And.NotContain("family").And.NotContain("friends");
    }

    [TestMethod]
    public void Prompts_EveryAgeGroupAndGender_AreDistinct()
    {
        var prompts = (from age in ModelPeople.AgeGroups
                       from gender in ModelPeople.Genders
                       select ModelPhotoPrompts.Build(new("single", age, gender)).Prompt).ToList();

        prompts.Should().OnlyHaveUniqueItems();
    }
}
