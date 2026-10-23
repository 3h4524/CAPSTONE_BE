using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.Application.Abstractions;
using APCS.Application.Features.TestReports;
using APCS.Application.Features.TestReports.Dtos;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace APCS.IntegrationTests.Admin;

/// <summary>
/// Covers the admin test-report endpoints over real HTTP and a real database.
/// </summary>
/// <remarks>
/// <para>
/// A run is written through the store and then read back through the endpoints, so the whole
/// path is proved: the row reaches the table, the listing finds it, and the detail turns the
/// stored report into the tests the pages show. The table is created from the committed script
/// because the test database is built from the bootstrap dump, which predates it.
/// </para>
/// </remarks>
[TestClass]
public sealed class TestReportEndpointTests
{
    private const string Route = "/api/admin/test-reports";
    private const string RunId = "run-integration-1";

    private const string ReportJson = """
    {
      "config": {},
      "suites": [
        {
          "title": "landing.spec.ts",
          "file": "e2e/public/landing.spec.ts",
          "column": 0,
          "line": 0,
          "specs": [
            {
              "title": "shows the hero",
              "ok": true,
              "tags": [],
              "id": "1",
              "file": "e2e/public/landing.spec.ts",
              "line": 5,
              "column": 3,
              "tests": [
                {
                  "timeout": 30000,
                  "expectedStatus": "passed",
                  "projectId": "public",
                  "projectName": "public",
                  "results": [
                    { "status": "passed", "duration": 90, "retry": 0, "error": null, "errors": [] }
                  ]
                }
              ]
            }
          ],
          "suites": []
        }
      ],
      "errors": [],
      "stats": {
        "startTime": "2026-10-22T02:45:54.530Z",
        "duration": 90.0,
        "expected": 1,
        "unexpected": 0,
        "flaky": 0,
        "skipped": 0
      }
    }
    """;

    private static IntegrationTestHost _host = null!;

    [ClassInitialize]
    public static async Task StartHostAsync(TestContext testContext)
    {
        _host = await IntegrationTestHost.StartAsync();
    }

    [ClassCleanup]
    public static async Task StopHostAsync()
    {
        await _host.DisposeAsync();
    }

    [TestInitialize]
    public async Task PrepareDatabaseAsync()
    {
        _host.EnsureAvailable();
        await _host.ResetDatabaseAsync();
        await EnsureTableAsync();
    }

    [TestMethod]
    public async Task List_WithAnAdminSession_ContainsTheStoredRun()
    {
        await StoreRunAsync();

        using var client = await AdminClientAsync();
        using var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("runId").GetString())
            .Should().Contain(RunId);
    }

    [TestMethod]
    public async Task Get_WithAnAdminSession_ReturnsTheTestsOfTheRun()
    {
        await StoreRunAsync();

        using var client = await AdminClientAsync();
        using var response = await client.GetAsync($"{Route}/{RunId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("run").GetProperty("status").GetString().Should().Be("passed");
        var files = body.GetProperty("files").EnumerateArray().ToList();
        files.Should().HaveCount(1);
        files[0].GetProperty("file").GetString().Should().Be("e2e/public/landing.spec.ts");
        files[0].GetProperty("specs").EnumerateArray()
            .Select(spec => spec.GetProperty("title").GetString())
            .Should().Contain("shows the hero");
    }

    [TestMethod]
    public async Task Delete_WithAnAdminSession_RemovesTheRun()
    {
        await StoreRunAsync();

        using var client = await AdminClientAsync();
        using var response = await client.DeleteAsync($"{Route}/{RunId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var listed = await client.GetAsync(Route);
        var body = await listed.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("runId").GetString())
            .Should().NotContain(RunId);
    }

    [TestMethod]
    public async Task Get_WithoutASession_IsRefused()
    {
        await StoreRunAsync();

        using var client = TestAccount.CreateAnonymousClient(_host);
        using var response = await client.GetAsync($"{Route}/{RunId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task StoreRunAsync()
    {
        using var scope = _host.Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITestReportStore>();

        await store.SaveRunAsync(
            new TestReportRunDto
            {
                RunId = RunId,
                StartedAtUtc = new DateTime(2026, 10, 22, 2, 45, 54, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 10, 22, 2, 47, 24, DateTimeKind.Utc),
                DurationMs = 90_000,
                Status = "passed",
                Branch = "feat/fe-test-reports",
                CommitSha = "abc1234",
                TriggeredBy = "admin@apcs.test",
                TotalTests = 1,
                PassedTests = 1,
                FileCount = 1,
                SuiteCount = 1,
            },
            ReportJson,
            CancellationToken.None);
    }

    private static async Task EnsureTableAsync()
    {
        var connectionString = _host.Factory.Services
            .GetRequiredService<IConfiguration>()["ConnectionStrings:DefaultConnection"];

        var script = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "scripts", "sql", "test-report-runs.sql"));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(script, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<HttpClient> AdminClientAsync()
    {
        var admin = await TestAccount.SignInAsync(_host, TestAccount.Unique("adminmgr") + "@apcs.test");
        var seeder = AuthTestSeeder.Create(_host.Factory.Services);
        var adminRoleId = await seeder.SeedRoleAsync(AuthConstants.AdminRole, AuthConstants.AdminRole);
        await seeder.GrantRoleAsync(admin.UserId, adminRoleId);

        var token = TestAuth.MintToken([TestAuth.AdminRole], admin.UserId);
        return _host.Factory.CreateAuthenticatedClient(token);
    }
}
