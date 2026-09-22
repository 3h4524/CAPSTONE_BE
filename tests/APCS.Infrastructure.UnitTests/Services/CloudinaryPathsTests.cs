using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public class CloudinaryPathsTests
{
    [TestMethod]
    [DataRow("folder1", "image1.jpg", "folder1/image1.jpg")]
    [DataRow("folder1/subfolder", "image1.jpg", "folder1/subfolder/image1.jpg")]
    [DataRow("", "image1.jpg", "image1.jpg")]
    [DataRow("  ", "image1.jpg", "image1.jpg")]
    [DataRow("folder1/", "image1.jpg", "folder1/image1.jpg")]
    [DataRow("\\folder1\\", "image1.jpg", "folder1/image1.jpg")]
    public void BuildPublicId_ReturnsExpectedResult(string folder, string storageKey, string expected)
    {
        var result = CloudinaryPaths.BuildPublicId(folder, storageKey);
        result.Should().Be(expected);
    }

    [TestMethod]
    [DataRow("folder1", "image1.jpg", "folder1")]
    [DataRow("folder1/subfolder", "image1.jpg", "folder1/subfolder")]
    [DataRow("", "image1.jpg", "")]
    [DataRow("folder1/subfolder/", "image1.jpg", "folder1/subfolder")]
    public void BuildAssetFolder_ReturnsExpectedResult(string folder, string storageKey, string expected)
    {
        var result = CloudinaryPaths.BuildAssetFolder(folder, storageKey);
        result.Should().Be(expected);
    }

    [TestMethod]
    [DataRow("folder1", "folder1")]
    [DataRow("folder1/subfolder", "folder1/subfolder")]
    [DataRow("folder1//subfolder", "folder1/subfolder")]
    [DataRow("\\folder1\\subfolder\\", "folder1/subfolder")]
    public void NormalizePath_ReturnsExpectedResult(string path, string expected)
    {
        var result = CloudinaryPaths.NormalizePath(path);
        result.Should().Be(expected);
    }
}
