using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;

namespace APCS.E2E;

public sealed record E2EOutcome(
    int ExitCode,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    string ReportPath,
    string PlaywrightLogPath,
    string LogTail);

public sealed class E2EOrchestrator(E2EOptions options)
{
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly int[] ApiPortCandidates = [5191, 5192, 5193, 5194, 5195];

    public async Task<E2EOutcome> RunAsync(CancellationToken cancellationToken = default)
    {
        return options.Command switch
        {
            "test" => await RunSuiteAsync(cancellationToken).ConfigureAwait(false),
            "up" => await BringUpAsync(cancellationToken).ConfigureAwait(false),
            "down" => await TearDownAsync(cancellationToken).ConfigureAwait(false),
            "clean" => await CleanDatabaseAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"{E2EOptions.CommandVariable} must be one of: test, up, down, clean (actual: '{options.Command}')."),
        };
    }

    private async Task<E2EOutcome> RunSuiteAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.RunDirectory);
        await using var lockHandle = AcquireLock();

        LaunchedProcess? smtpSink = null;
        try
        {
            await BringInfraUpAsync(cancellationToken).ConfigureAwait(false);
            smtpSink = StartSmtpSink();

            var (apiProcess, apiPort) = StartApi();
            try
            {
                await WaitUntilHttpHealthyAsync("API", $"http://localhost:{apiPort}/health", apiProcess, cancellationToken)
                    .ConfigureAwait(false);
                await WaitUntilSmtpReadyAsync(smtpSink, cancellationToken).ConfigureAwait(false);

                await new SellerSeeder(options).RegisterVerifiedSellerAsync(cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"[e2e] seller seeded: {options.SellerEmail}");

                return await RunPlaywrightAsync(apiPort, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                apiProcess.KillTree();
            }
        }
        finally
        {
            smtpSink?.KillTree();
            await ComposeAsync("down", "-v", "--remove-orphans").ConfigureAwait(false);
        }
    }

    private async Task<E2EOutcome> BringUpAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.RunDirectory);
        await using var lockHandle = AcquireLock();

        await BringInfraUpAsync(cancellationToken).ConfigureAwait(false);
        var smtpSink = StartSmtpSink();
        var (apiProcess, apiPort) = StartApi();

        await WaitUntilHttpHealthyAsync("API", $"http://localhost:{apiPort}/health", apiProcess, cancellationToken)
            .ConfigureAwait(false);
        await WaitUntilSmtpReadyAsync(smtpSink, cancellationToken).ConfigureAwait(false);

        await new SellerSeeder(options).RegisterVerifiedSellerAsync(cancellationToken).ConfigureAwait(false);

        File.WriteAllText(Path.Combine(options.RunDirectory, "api.pid"), apiProcess.Pid.ToString());
        File.WriteAllText(Path.Combine(options.RunDirectory, "smtp.pid"), smtpSink.Pid.ToString());

        Console.WriteLine($"[e2e] API up on http://localhost:{apiPort} (pid {apiProcess.Pid}, pid file api.pid)");
        Console.WriteLine($"[e2e] smtp sink up on 127.0.0.1:{options.SmtpPort} (pid {smtpSink.Pid}, pid file smtp.pid)");
        Console.WriteLine("[e2e] stop it later with E2E_ORCHESTRATION_COMMAND=down");

        return new E2EOutcome(0, 0, 0, 0, 0, options.ReportPath, string.Empty, string.Empty);
    }

    private async Task<E2EOutcome> TearDownAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.RunDirectory);

        ProcessLauncher.KillPidFromFile(Path.Combine(options.RunDirectory, "api.pid"));
        ProcessLauncher.KillPidFromFile(Path.Combine(options.RunDirectory, "smtp.pid"));
        await ComposeAsync("down", "-v", "--remove-orphans").ConfigureAwait(false);
        File.Delete(Path.Combine(options.RunDirectory, "api.pid"));
        File.Delete(Path.Combine(options.RunDirectory, "smtp.pid"));

        Console.WriteLine("[e2e] test stack stopped");
        return new E2EOutcome(0, 0, 0, 0, 0, options.ReportPath, string.Empty, string.Empty);
    }

    private async Task<E2EOutcome> CleanDatabaseAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.RunDirectory);
        await using var lockHandle = AcquireLock();

        await EnsureComposeUpAsync(cancellationToken).ConfigureAwait(false);
        await EnsureRoleAndDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await ApplyModelSchemaAsync(cancellationToken).ConfigureAwait(false);

        Console.WriteLine("[e2e] apcs_e2e deleted and recreated from the EF model");
        return new E2EOutcome(0, 0, 0, 0, 0, options.ReportPath, string.Empty, string.Empty);
    }

    private async Task BringInfraUpAsync(CancellationToken cancellationToken)
    {
        await EnsureComposeUpAsync(cancellationToken).ConfigureAwait(false);
        await EnsureRoleAndDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await ApplyModelSchemaAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureComposeUpAsync(CancellationToken cancellationToken)
    {
        await ComposeAsync("up", "-d", "--wait", "--quiet-pull").ConfigureAwait(false);
    }

    private async Task ComposeAsync(params string[] arguments)
    {
        var (exitCode, logTail) = await RunAndCaptureAsync(
            "docker",
            ["compose", "-f", options.ComposeFile, .. arguments],
            options.RepositoryRoot,
            ComposeEnvironment(),
            TimeSpan.FromMinutes(5));

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"docker compose failed (exit {exitCode}).{Environment.NewLine}{logTail}");
        }
    }

    private async Task EnsureRoleAndDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(options.PostgresConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var roleExists = await new NpgsqlCommand(
            "SELECT 1 FROM pg_roles WHERE rolname = @name",
            connection)
        {
            Parameters = { new("name", options.DbRole) },
        }.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (roleExists is null)
        {
            await new NpgsqlCommand(
                $"CREATE ROLE \"{options.DbRole}\" LOGIN SUPERUSER PASSWORD '{options.DbPassword}'",
                connection)
                .ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var databaseExists = await new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name",
            connection)
        {
            Parameters = { new("name", options.DbName) },
        }.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (databaseExists is null)
        {
            await new NpgsqlCommand(
                $"CREATE DATABASE \"{options.DbName}\" OWNER \"{options.DbRole}\"",
                connection)
                .ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyModelSchemaAsync(CancellationToken cancellationToken)
    {
        await using (var dataSource = NpgsqlDataSource.Create(options.PostgresConnectionString))
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{options.DbName}\" WITH (FORCE)",
                connection)
                .ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await new NpgsqlCommand(
                $"CREATE DATABASE \"{options.DbName}\" OWNER \"{options.DbRole}\"",
                connection)
                .ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var dataSource = NpgsqlDataSource.Create(options.ApiDbConnectionString))
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\"", connection)
                .ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS \"vector\"", connection)
                    .ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (exception.SqlState == "58P01")
            {
                throw new InvalidOperationException(
                    "The Postgres server of the test infrastructure has no pgvector extension. "
                    + "docker-compose.yml must use the image pgvector/pgvector:pg18.",
                    exception);
            }
        }

        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(options.ApiDbConnectionString, npgsql => npgsql.UseVector())
                .Options);

        await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        context.AddRange(BuildSystemPresets());
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<APCS.Domain.Entities.StyleArtPreset> BuildSystemPresets()
    {
        var now = DateTime.UtcNow;

        return
        [
            new APCS.Domain.Entities.StyleArtPreset
            {
                Id = Guid.NewGuid(),
                Name = "Line Art Illustration",
                Description = "Clean black line art with flat accents.",
                StyleModifiers = """
                    {"style":"line art","prompt_additions":"flat vector shapes, uniform line weight, minimal shading"}
                    """,
                Recommendations = "[]",
                IsSystemTemplate = true,
                IsActive = true,
                UsageCount = 0,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new APCS.Domain.Entities.StyleArtPreset
            {
                Id = Guid.NewGuid(),
                Name = "Soft Watercolor",
                Description = "Pastel watercolor washes.",
                StyleModifiers = """
                    {"style":"watercolor","prompt_additions":"soft pastel palette, paper texture, gentle gradients"}
                    """,
                Recommendations = "[]",
                IsSystemTemplate = true,
                IsActive = true,
                UsageCount = 0,
                CreatedAt = now,
                UpdatedAt = now,
            },
        ];
    }

    private LaunchedProcess StartSmtpSink()
    {
        if (IsPortOpen(options.SmtpPort))
        {
            throw new InvalidOperationException(
                $"TCP port {options.SmtpPort} is already in use; another smtp sink or foreign process took it.");
        }

        return ProcessLauncher.Start(
            "python3",
            [
                options.SmtpSinkScript,
                "--host",
                "127.0.0.1",
                "--port",
                options.SmtpPort.ToString(),
                "--out",
                options.MailFile,
                "--pid-file",
                Path.Combine(options.RunDirectory, "smtp.pid"),
                "--reset",
            ],
            options.RepositoryRoot,
            null,
            Path.Combine(options.RunDirectory, "smtp.log"),
            "smtp-sink");
    }

    private (LaunchedProcess Process, int ApiPort) StartApi()
    {
        var apiPort = ResolveFreeApiPort();
        var process = ProcessLauncher.Start(
            "dotnet",
            ["run", "--project", options.ApiProject, "--no-launch-profile", "-c", "Release"],
            options.RepositoryRoot,
            ApiEnvironment(apiPort),
            Path.Combine(options.RunDirectory, "api.log"),
            "api");

        return (process, apiPort);
    }

    private int ResolveFreeApiPort()
    {
        var port = ApiPortCandidates.First(candidate => options.ApiPort == candidate || !IsPortOpen(candidate));

        if (port != options.ApiPort)
        {
            Console.WriteLine($"[e2e] API port {options.ApiPort} is busy; using {port} instead");
        }

        return port;
    }

    private Dictionary<string, string> ApiEnvironment(int apiPort)
    {
        return new Dictionary<string, string>
        {
            ["ConnectionStrings__DefaultConnection"] =
                $"Host=127.0.0.1;Port={options.PostgresPort};Database={options.DbName};Username={options.DbRole};Password={options.DbPassword};SSL Mode=Disable",
            ["Redis__ConnectionString"] = $"127.0.0.1:{options.RedisPort},abortConnect=false",
            ["Redis__InstanceName"] = "APCS:e2e:",
            ["Redis__DefaultExpirationMinutes"] = "10",
            ["Jwt__Issuer"] = "APCS.E2E",
            ["Jwt__Audience"] = "APCS.Web",
            ["Jwt__SigningKey"] = options.JwtSigningKey,
            ["Jwt__AccessTokenMinutes"] = "30",
            ["Jwt__RefreshTokenDays"] = "7",
            ["Jwt__EmailVerificationHours"] = "24",
            ["Jwt__PasswordResetMinutes"] = "30",
            ["App__BaseUrl"] = $"http://localhost:{options.FrontendPort}",
            ["App__VerifyEmailPath"] = "/verify-email",
            ["App__ResetPasswordPath"] = "/reset-password",
            ["Cors__AllowedOrigins"] = "http://localhost:4010;http://localhost:4020",
            ["Smtp__Host"] = "127.0.0.1",
            ["Smtp__Port"] = options.SmtpPort.ToString(),
            ["Smtp__UseStartTls"] = "false",
            ["Smtp__Username"] = "e2e",
            ["Smtp__Password"] = "e2e",
            ["Smtp__FromAddress"] = "e2e@test.local",
            ["Smtp__FromName"] = "APCS E2E",
            ["Cloudinary__CloudName"] = "apcs-e2e-stub",
            ["Cloudinary__ApiKey"] = "e2e-stub-key",
            ["Cloudinary__ApiSecret"] = "e2e-stub-secret",
            ["Cloudinary__Folder"] = "e2e",
            ["Cloudinary__SignedUrlTtlMinutes"] = "10",
            ["PayOS__ClientId"] = "",
            ["PayOS__ApiKey"] = "",
            ["PayOS__ChecksumKey"] = "",
            ["PayOS__UsdToVndRate"] = "25000",
            ["PayOS__TestAmountVnd"] = "0",
            ["ASPNETCORE_ENVIRONMENT"] = "E2E",
            ["DOTNET_ENVIRONMENT"] = "E2E",
            ["ASPNETCORE_URLS"] = $"http://localhost:{apiPort}",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["Logging__LogLevel__Default"] = "Information",
            ["Logging__LogLevel__Microsoft_AspNetCore"] = "Warning",
        };
    }

    private Dictionary<string, string> PlaywrightEnvironment(int apiPort)
    {
        var environment = new Dictionary<string, string>
        {
            ["E2E_BASE_URL"] = $"http://localhost:{options.FrontendPort}",
            ["E2E_API_BASE_URL"] = $"http://localhost:{apiPort}",
            ["E2E_SELLER_EMAIL"] = options.SellerEmail,
            ["E2E_SELLER_PASSWORD"] = options.SellerPassword,
            ["E2E_SMTP_FILE"] = options.MailFile,
            ["E2E_RUN_DIR"] = options.RunDirectory,
            ["CI"] = "",
            ["NO_PROXY"] = "localhost,127.0.0.1,::1",
        };

        var headed = Environment.GetEnvironmentVariable("E2E_HEADED");
        if (!string.IsNullOrWhiteSpace(headed))
        {
            environment["E2E_HEADED"] = headed;
        }

        return environment;
    }

    private async Task WaitUntilHttpHealthyAsync(
        string label,
        string url,
        LaunchedProcess process,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + options.HealthTimeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.Inner is null)
            {
                var tail = await ReadTailAsync(process.LogFilePath, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"The {label} process exited before becoming healthy.{Environment.NewLine}{tail}");
            }

            try
            {
                using var response = await HealthClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                && !cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }

        var logTail = await ReadTailAsync(process.LogFilePath, cancellationToken).ConfigureAwait(false);
        process.KillTree();

        throw new InvalidOperationException(
            $"The {label} did not become healthy within {options.HealthTimeout.TotalMinutes:F0} minutes at {url}.{Environment.NewLine}{logTail}");
    }

    private async Task WaitUntilSmtpReadyAsync(LaunchedProcess smtpSink, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + options.HealthTimeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (smtpSink.Inner is null)
            {
                var tail = await ReadTailAsync(smtpSink.LogFilePath, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException($"The smtp sink exited.{Environment.NewLine}{tail}");
            }

            if (IsPortOpen(options.SmtpPort))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        var logTail = await ReadTailAsync(smtpSink.LogFilePath, cancellationToken).ConfigureAwait(false);
        smtpSink.KillTree();

        throw new InvalidOperationException(
            $"The smtp sink did not start listening on port {options.SmtpPort} in time.{Environment.NewLine}{logTail}");
    }

    private async Task<E2EOutcome> RunPlaywrightAsync(int apiPort, CancellationToken cancellationToken)
    {
        var launched = ProcessLauncher.StartScript(
            "pnpm test:e2e",
            options.FrontendRoot,
            PlaywrightEnvironment(apiPort),
            Path.Combine(options.RunDirectory, "playwright.log"),
            "playwright");

        var (exitCode, logTail) = await ProcessLauncher
            .WaitForExitAsync(launched, options.SuiteTimeout, cancellationToken)
            .ConfigureAwait(false);

        return BuildOutcome(exitCode, launched.LogFilePath, logTail);
    }

    private E2EOutcome BuildOutcome(int exitCode, string playwrightLogPath, string logTail)
    {
        if (!File.Exists(options.ReportPath))
        {
            throw new InvalidOperationException(
                $"Playwright wrote no JSON report at {options.ReportPath} (suite exit {exitCode}).{Environment.NewLine}{logTail}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(options.ReportPath));
        var stats = document.RootElement.GetProperty("stats");

        var passed = ReadStat(stats, "expected", "passes");
        var failed = ReadStat(stats, "unexpected", "failures");
        var skipped = ReadStat(stats, "skipped");

        return new E2EOutcome(
            exitCode,
            passed + failed + skipped,
            passed,
            failed,
            skipped,
            options.ReportPath,
            playwrightLogPath,
            logTail);
    }

    private async Task<(int ExitCode, string LogTail)> RunAndCaptureAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout)
    {
        var launched = ProcessLauncher.Start(
            fileName,
            arguments,
            workingDirectory,
            environment,
            Path.Combine(Path.GetTempPath(), $"apcs-e2e-{Guid.NewGuid():N}.log"),
            fileName);

        var (exitCode, logTail) = await ProcessLauncher
            .WaitForExitAsync(launched, timeout, CancellationToken.None)
            .ConfigureAwait(false);

        return (exitCode, logTail);
    }

    private IReadOnlyDictionary<string, string> ComposeEnvironment()
    {
        return new Dictionary<string, string>
        {
            ["TEST_PG_PORT"] = options.PostgresPort.ToString(),
            ["TEST_REDIS_PORT"] = options.RedisPort.ToString(),
            ["TEST_DB_USER"] = options.Superuser,
            ["TEST_DB_PASSWORD"] = options.SuperPassword,
        };
    }

    private Task<string> ReadTailAsync(string logFilePath, CancellationToken cancellationToken) =>
        ProcessLauncher.ReadTailAsync(logFilePath, cancellationToken);

    private IAsyncDisposable AcquireLock()
    {
        var path = Path.Combine(options.RunDirectory, "orchestration.lock");

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "Another E2E orchestration is already running; stop it with E2E_ORCHESTRATION_COMMAND=down first.",
                exception);
        }

        return new OrchestrationLock(stream, path);
    }

    private sealed class OrchestrationLock(FileStream stream, string path) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stream.Dispose();

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }

            return ValueTask.CompletedTask;
        }
    }

    private static bool IsPortOpen(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.BeginConnect(IPAddress.Loopback, port, null, null);
            return connect.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1)) && client.Connected;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int ReadStat(JsonElement stats, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (stats.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetInt32();
            }
        }

        return 0;
    }
}
