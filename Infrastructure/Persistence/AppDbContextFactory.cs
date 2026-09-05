using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace APCS.Infrastructure.Persistence;

/// <summary>
/// Builds an <see cref="AppDbContext"/> for the EF Core command-line tools.
/// </summary>
/// <remarks>
/// Design-time commands such as <c>dotnet ef migrations add</c> only need the model, not a live
/// database, so this bypasses the application host and its configuration requirements. Point
/// <c>APCS_MIGRATIONS_CONNECTION</c> at a real database when running commands that do connect,
/// such as <c>database update</c>.
/// </remarks>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string ConnectionStringVariable = "APCS_MIGRATIONS_CONNECTION";

    private const string PlaceholderConnectionString =
        "Host=localhost;Port=5432;Database=apcs;Username=postgres;Password=postgres";

    /// <inheritdoc />
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? PlaceholderConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options, TimeProvider.System);
    }
}
