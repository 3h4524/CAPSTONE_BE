using APCS.Infrastructure.Services;
using FluentAssertions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class BiRefNetGarmentSegmenterTests
{
    [TestMethod]
    public void ResolvePath_LooksInTheContentRootAndThenItsParent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"apcs-model-{Guid.NewGuid():N}");
        var contentRoot = Path.Combine(root, "API");
        Directory.CreateDirectory(Path.Combine(root, "models"));
        Directory.CreateDirectory(contentRoot);
        var model = Path.Combine(root, "models", "model.onnx");
        File.WriteAllBytes(model, [1]);
        try
        {
            BiRefNetGarmentSegmenter.ResolvePath("models/model.onnx", contentRoot).Should().Be(Path.GetFullPath(model));
            BiRefNetGarmentSegmenter.ResolvePath("models/missing.onnx", contentRoot).Should().BeNull();
            BiRefNetGarmentSegmenter.ResolvePath(model, contentRoot).Should().Be(model);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
