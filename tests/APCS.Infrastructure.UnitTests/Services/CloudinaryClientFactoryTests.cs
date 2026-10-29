using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public class CloudinaryClientFactoryTests
{
    [TestMethod]
    public void Create_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => CloudinaryClientFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void Create_OptionsNotConfigured_ThrowsInvalidOperationException()
    {
        var options = new CloudinaryOptions();
        Action act = () => CloudinaryClientFactory.Create(options);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cloudinary storage is not configured*");
    }

    [TestMethod]
    public void Create_ValidOptions_ReturnsCloudinaryInstance()
    {
        var options = new CloudinaryOptions
        {
            CloudName = "testcloud",
            ApiKey = "testkey",
            ApiSecret = "testsecret"
        };
        
        var result = CloudinaryClientFactory.Create(options);
        
        result.Should().NotBeNull();
        result.Api.Secure.Should().BeTrue();
    }
}
