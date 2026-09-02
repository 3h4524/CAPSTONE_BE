using APCS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace APCS.Infrastructure.UnitTests.TestSupport;

internal sealed class IdentityManagerMocks
{
    public IdentityManagerMocks()
    {
        var userStore = new Mock<IUserStore<Seller>>();
        UserManager = new Mock<UserManager<Seller>>(
            userStore.Object,
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<Seller>(),
            Array.Empty<IUserValidator<Seller>>(),
            Array.Empty<IPasswordValidator<Seller>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<Seller>>>());

        SignInManager = new Mock<SignInManager<Seller>>(
            UserManager.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<Seller>>(),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            Mock.Of<ILogger<SignInManager<Seller>>>(),
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<Seller>>());

        var roleStore = new Mock<IRoleStore<IdentityRole<int>>>();
        RoleManager = new Mock<RoleManager<IdentityRole<int>>>(
            roleStore.Object,
            Array.Empty<IRoleValidator<IdentityRole<int>>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Mock.Of<ILogger<RoleManager<IdentityRole<int>>>>());
    }

    public Mock<UserManager<Seller>> UserManager { get; }

    public Mock<SignInManager<Seller>> SignInManager { get; }

    public Mock<RoleManager<IdentityRole<int>>> RoleManager { get; }
}
