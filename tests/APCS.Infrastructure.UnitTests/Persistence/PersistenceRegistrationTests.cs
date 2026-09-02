using APCS.Application.Abstractions.Persistence;
using APCS.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace APCS.Infrastructure.UnitTests.Persistence;

[TestClass]
public sealed class PersistenceRegistrationTests
{
    [TestMethod]
    public void AddInfrastructure_WithinOneScope_ReusesAppDbContextForPersistenceAbstractions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Database=apcs_tests;Username=postgres;Password=postgres",
                ["Jwt:SigningKey"] = new string('k', 32),
                ["Redis:ConnectionString"] = "localhost:6379"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var readDbContext = scope.ServiceProvider.GetRequiredService<IReadDbContext>();

        unitOfWork.Should().BeOfType<AppDbContext>();
        readDbContext.Should().BeSameAs(unitOfWork);
    }
}
