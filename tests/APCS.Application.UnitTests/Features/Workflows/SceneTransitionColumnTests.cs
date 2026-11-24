using APCS.Application.Features.Workflows;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class SceneTransitionColumnTests
{
    /// <summary>The transitions a video can have: what the settings validator and the media worker accept.</summary>
    private static readonly string[] VideoTransitions = ["none", "fade", "swipe", "zoom", "flash", "whip_pan", "blur", "cut"];

    [TestMethod]
    public void For_EveryTransitionAVideoCanHave_IsAWordTheColumnAllows()
    {
        // A word the column refuses fails the whole save (chk_scene_transition), so none may get through.
        foreach (var transition in VideoTransitions)
            SceneTransitionColumn.Allowed.Should().Contain(SceneTransitionColumn.For(transition)!, $"'{transition}' is written to the column");
    }

    [TestMethod]
    [DataRow("fade", "fade")]
    [DataRow("zoom", "zoom")]
    [DataRow("cut", "cut")]
    [DataRow("none", "none")]
    [DataRow("swipe", "slide")]
    [DataRow("whip_pan", "slide")]
    [DataRow("flash", "fade")]
    [DataRow("blur", "fade")]
    public void For_KnownTransition_KeepsItOrTakesTheNearestWord(string transition, string expected)
    {
        SceneTransitionColumn.For(transition).Should().Be(expected);
    }

    [TestMethod]
    public void For_NoTransition_IsNoValue()
    {
        SceneTransitionColumn.For(null).Should().BeNull();
    }

    [TestMethod]
    public void For_UnknownWord_ReadsAsNoTransitionInsteadOfFailingTheSave()
    {
        SceneTransitionColumn.For("spin_around").Should().Be("none");
    }
}
