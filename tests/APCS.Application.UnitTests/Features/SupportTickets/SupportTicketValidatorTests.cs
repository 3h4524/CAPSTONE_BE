using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.SupportTickets.Dtos.Request;
using APCS.Application.Features.SupportTickets.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.SupportTickets;

[TestClass]
public sealed class SupportTicketValidatorTests
{
    [TestMethod]
    public async Task CreateValidator_WithSupportedContract_IsValid()
    {
        var request = new CreateSupportTicketRequestDto(
            "Etsy publish error",
            "integration",
            "normal",
            "Publishing the draft returns a permissions error.",
            [File("context.pdf", "application/pdf", 1024)]);

        var result = await new CreateSupportTicketValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("unknown", "normal")]
    [DataRow("integration", "medium")]
    public async Task CreateValidator_WithUnknownCategoryOrPriority_IsInvalid(
        string category,
        string priority)
    {
        var request = new CreateSupportTicketRequestDto(
            "Etsy publish error",
            category,
            priority,
            "Publishing the draft returns a permissions error.",
            []);

        var result = await new CreateSupportTicketValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [TestMethod]
    public async Task CreateValidator_WithMoreThanFiveFiles_IsInvalid()
    {
        var files = Enumerable.Range(0, 6)
            .Select(index => File($"evidence-{index}.txt", "text/plain", 100))
            .ToArray();
        var request = ValidRequest(files);

        var result = await new CreateSupportTicketValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error => error.PropertyName == "Attachments");
    }

    [TestMethod]
    [DataRow("evidence.exe", "application/octet-stream", 1024)]
    [DataRow("evidence.pdf", "application/x-msdownload", 1024)]
    [DataRow("evidence.pdf", "application/pdf", 10 * 1024 * 1024 + 1)]
    public async Task AttachmentValidator_WithUnsafeMetadata_IsInvalid(
        string fileName,
        string contentType,
        int length)
    {
        var result = await new SupportTicketAttachmentValidator().ValidateAsync(
            File(fileName, contentType, length));

        result.IsValid.Should().BeFalse();
    }

    [TestMethod]
    public async Task CreateValidator_WithMoreThanTwentyFiveMegabytesTotal_IsInvalid()
    {
        var request = ValidRequest(
        [
            File("one.pdf", "application/pdf", 9 * 1024 * 1024),
            File("two.pdf", "application/pdf", 9 * 1024 * 1024),
            File("three.pdf", "application/pdf", 8 * 1024 * 1024)
        ]);

        var result = await new CreateSupportTicketValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error => error.PropertyName == "Attachments");
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, true)]
    [DataRow(5, true)]
    [DataRow(6, false)]
    public async Task RatingValidator_EnforcesOneToFive(int rating, bool expectedValid)
    {
        var result = await new RateSupportTicketValidator().ValidateAsync(
            new RateSupportTicketRequestDto(rating));

        result.IsValid.Should().Be(expectedValid);
    }

    private static CreateSupportTicketRequestDto ValidRequest(IReadOnlyCollection<UploadFileDto> files) =>
        new(
            "Etsy publish error",
            "integration",
            "normal",
            "Publishing the draft returns a permissions error.",
            files);

    private static UploadFileDto File(string name, string contentType, int length) =>
        new(name, contentType, length, new MemoryStream(new byte[Math.Min(length, 1024)]));
}
