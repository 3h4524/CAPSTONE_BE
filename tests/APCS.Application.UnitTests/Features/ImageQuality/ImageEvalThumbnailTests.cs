using APCS.Application.Features.ImageQuality.Common;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.ImageQuality;

[TestClass]
public sealed class ImageEvalThumbnailTests
{
    internal static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();

    internal static string DataUrl(string mimeType, byte[] bytes) => $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";

    [TestMethod]
    public void Decode_AcceptsJpegPngAndWebp()
    {
        ImageEvalThumbnail.Decode(DataUrl("image/jpeg", Jpeg))!.Extension.Should().Be("jpg");
        ImageEvalThumbnail.Decode(DataUrl("image/png", Png))!.MimeType.Should().Be("image/png");
        ImageEvalThumbnail.Decode(DataUrl("image/webp", Webp))!.Bytes.Should().Equal(Webp);
    }

    [TestMethod]
    public void Decode_BytesThatAreNotTheClaimedType_AreRefused()
    {
        ImageEvalThumbnail.Decode(DataUrl("image/jpeg", Png)).Should().BeNull();
    }

    [TestMethod]
    public void Decode_OtherTypesAndBrokenBase64_AreRefused()
    {
        ImageEvalThumbnail.Decode(DataUrl("image/gif", Jpeg)).Should().BeNull();
        ImageEvalThumbnail.Decode("data:image/jpeg;base64,***").Should().BeNull();
        ImageEvalThumbnail.Decode("").Should().BeNull();
    }

    [TestMethod]
    public void Decode_ThumbnailOverTheLimit_IsRefused()
    {
        var large = new byte[ImageEvalThumbnail.MaximumBytes + 1];
        Jpeg.CopyTo(large, 0);

        ImageEvalThumbnail.Decode(DataUrl("image/jpeg", large)).Should().BeNull();
    }
}
