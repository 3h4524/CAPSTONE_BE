using System;
using System.IO;

namespace APCS.E2E;

public sealed class E2EOptions
{
    public const string CommandVariable = "E2E_ORCHESTRATION_COMMAND";

    public string Command { get; init; } = "test";

    public required string RepositoryRoot { get; init; }

    public required string FrontendRoot { get; init; }

    public required string RunDirectory { get; init; }

    public int ApiPort { get; init; } = 5191;

    public int FrontendPort { get; init; } = 4010;

    public int SmtpPort { get; init; } = 2525;

    public int PostgresPort { get; init; } = 55432;

    public int RedisPort { get; init; } = 56379;

    public string DbName { get; init; } = "apcs_e2e";

    public string DbRole { get; init; } = "apcs_e2e";

    public string DbPassword { get; init; } = "apcs_e2e";

    public string Superuser { get; init; } = "apcs";

    public string SuperPassword { get; init; } = "apcs";

    public string JwtSigningKey { get; init; } = "apcs.e2e.signing.key.0123456789abcdef";

    public string SellerEmail { get; init; } = "e2e.seller@apcs.test";

    public string SellerPassword { get; init; } = "Passw0rdTest1!";

    public string SellerFullName { get; init; } = "E2E Seller";

    public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan SuiteTimeout { get; init; } = TimeSpan.FromMinutes(60);

    public string ComposeFile => Path.Combine(RepositoryRoot, "scripts", "e2e", "docker-compose.yml");

    public string ApiProject => Path.Combine(RepositoryRoot, "API", "API.csproj");

    public string SmtpSinkScript => Path.Combine(RepositoryRoot, "scripts", "e2e", "smtp-sink.py");

    public string MailFile => Path.Combine(RunDirectory, "mail.jsonl");

    public string ReportPath => Path.Combine(FrontendRoot, "test-results", "report", "report.json");

    public string PostgresConnectionString =>
        $"Host=127.0.0.1;Port={PostgresPort};Database=postgres;Username={Superuser};Password={SuperPassword};SSL Mode=Disable";

    public string ApiDbConnectionString =>
        $"Host=127.0.0.1;Port={PostgresPort};Database={DbName};Username={DbRole};Password={DbPassword};SSL Mode=Disable";

    public static E2EOptions FromEnvironment()
    {
        var repositoryRoot = Read("E2E_BE_ROOT") ?? DiscoverRepositoryRoot();
        var runDirectory = Path.Combine(Path.GetFullPath(repositoryRoot), "scripts", "e2e", ".run");

        return new E2EOptions
        {
            Command = Read(CommandVariable) ?? "test",
            RepositoryRoot = Path.GetFullPath(repositoryRoot),
            FrontendRoot = Path.GetFullPath(Read("E2E_FE_ROOT") ?? Path.Combine(repositoryRoot, "..", "CAPSTONE_FE")),
            RunDirectory = runDirectory,
            ApiPort = ReadInt("E2E_API_PORT") ?? 5191,
            SmtpPort = ReadInt("E2E_SMTP_PORT") ?? 2525,
            PostgresPort = ReadInt("TEST_PG_PORT") ?? 55432,
            RedisPort = ReadInt("TEST_REDIS_PORT") ?? 56379,
            DbName = Read("E2E_DB") ?? "apcs_e2e",
            DbRole = Read("E2E_DB_USER") ?? "apcs_e2e",
            DbPassword = Read("E2E_DB_PASSWORD") ?? "apcs_e2e",
            Superuser = Read("E2E_PG_SUPERUSER") ?? "apcs",
            SuperPassword = Read("E2E_PG_SUPERPASSWORD") ?? "apcs",
            JwtSigningKey = Read("E2E_JWT_SIGNING_KEY") ?? "apcs.e2e.signing.key.0123456789abcdef",
            SellerEmail = Read("E2E_SELLER_EMAIL") ?? "e2e.seller@apcs.test",
            SellerPassword = Read("E2E_SELLER_PASSWORD") ?? "Passw0rdTest1!",
            SellerFullName = Read("E2E_SELLER_FULL_NAME") ?? "E2E Seller",
            HealthTimeout = TimeSpan.FromSeconds(ReadInt("E2E_HEALTH_TIMEOUT_SECONDS") ?? 300),
            SuiteTimeout = TimeSpan.FromMinutes(ReadInt("E2E_SUITE_TIMEOUT_MINUTES") ?? 60),
        };
    }

    private static string DiscoverRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "API", "API.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "The automation test could not locate the backend repository root. Set E2E_BE_ROOT to the path of CAPSTONE_BE.");
    }

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? ReadInt(string name)
    {
        var value = Read(name);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }
}
