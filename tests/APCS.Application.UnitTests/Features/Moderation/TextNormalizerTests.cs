using APCS.Application.Features.Moderation.Common;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Moderation;

[TestClass]
public sealed class TextNormalizerTests
{
    [TestMethod]
    [DataRow("S3X B@BY", "sex baby")]
    [DataRow("b00bs", "boobs")]
    [DataRow("s.e.x", "sex")]
    [DataRow("s e x", "sex")]
    [DataRow("s-e-x tee", "sex tee")]
    public void Normalize_UndoesLookAlikesAndSpacedLetters(string text, string expected)
    {
        TextNormalizer.Normalize(text).Should().Be(expected);
    }

    [TestMethod]
    [DataRow("2048 x 2048", "2048 x 2048")]
    [DataRow("t-shirt", "t-shirt")]
    [DataRow("Class of 2026", "class of 2026")]
    public void Normalize_LeavesOrdinaryTextAlone(string text, string expected)
    {
        TextNormalizer.Normalize(text).Should().Be(expected);
    }

    [TestMethod]
    public void Normalize_KeepsVietnameseAccents()
    {
        TextNormalizer.Normalize("Áo Thun Đẹp").Should().Be("áo thun đẹp");
    }

    [TestMethod]
    public void RemoveAccents_ReadsDWithStrokeAsD()
    {
        TextNormalizer.RemoveAccents("đụ địt ấu dâm").Should().Be("du dit au dam");
    }
}
