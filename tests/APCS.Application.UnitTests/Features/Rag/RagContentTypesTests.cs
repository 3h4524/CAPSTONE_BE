using APCS.Application.Features.Rag.Common;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Rag;

[TestClass]
public sealed class RagContentTypesTests
{
    [TestMethod]
    [DataRow("listing")]
    [DataRow("image")]
    [DataRow("video")]
    [DataRow("policy")]
    [DataRow("keyword")]
    [DataRow("faq")]
    public void IsSupported_KnownContentType_ReturnsTrue(string value) =>
        RagContentTypes.IsSupported(value).Should().BeTrue();

    [TestMethod]
    [DataRow("")]
    [DataRow("FAQ")]
    [DataRow("article")]
    public void IsSupported_UnknownContentType_ReturnsFalse(string value) =>
        RagContentTypes.IsSupported(value).Should().BeFalse();
}
