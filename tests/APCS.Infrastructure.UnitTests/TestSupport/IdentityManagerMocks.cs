using APCS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;

namespace APCS.Infrastructure.UnitTests.TestSupport;

internal sealed class IdentityManagerMocks
{
    public IdentityManagerMocks()
    {
        var userStore = new Mock<IUserStore<User>>();
        UserManager = new Mock<UserManager<User>>(
            userStore.Object,
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<User>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<User>>>());

        SignInManager = new Mock<SignInManager<User>>(
            UserManager.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<User>>(),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            Mock.Of<ILogger<SignInManager<User>>>(),
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<User>>());

        var roleStore = new Mock<IRoleStore<Role>>();
        RoleManager = new Mock<RoleManager<Role>>(
            roleStore.Object,
            Array.Empty<IRoleValidator<Role>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Mock.Of<ILogger<RoleManager<Role>>>());
    }

    public Mock<UserManager<User>> UserManager { get; }

    public Mock<SignInManager<User>> SignInManager { get; }

    public Mock<RoleManager<Role>> RoleManager { get; }
}
