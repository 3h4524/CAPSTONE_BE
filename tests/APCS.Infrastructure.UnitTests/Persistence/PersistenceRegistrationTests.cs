using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace APCS.Infrastructure.UnitTests.Persistence;

[TestClass]
public sealed class PersistenceRegistrationTests
{
    [TestMethod]
    public void AddInfrastructure_WithinOneScope_RegistersPersistenceAbstractions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Database=apcs_tests;Username=postgres;Password=postgres",
                ["Jwt:SigningKey"] = new string('k', 32),
                ["Redis:ConnectionString"] = "localhost:6379",
                ["App:BaseUrl"] = "http://localhost:3000"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var userRepository = scope.ServiceProvider.GetRequiredService<IRepository<User>>();
        var accountRepository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var accountService = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        unitOfWork.Should().BeOfType<AppDbContext>();
        userRepository.Should().BeOfType<Repository<User>>();
        accountRepository.Should().BeOfType<AccountRepository>();
        accountService.Should().BeOfType<AccountService>();
        passwordHasher.Should().BeOfType<PasswordHasher<User>>();
    }
}
