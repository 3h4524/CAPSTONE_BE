using System;
using APCS.Application.Features.TestReports;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.TestReports;

[TestClass]
public sealed class PlaywrightReportParserTests
{
    private const string ReportWithOneFailure = """
    {
      "config": {},
      "suites": [
        {
          "title": "profile.spec.ts",
          "file": "e2e/private/profile.spec.ts",
          "column": 0,
          "line": 0,
          "specs": [],
          "suites": [
            {
              "title": "profile",
              "file": "e2e/private/profile.spec.ts",
              "column": 0,
              "line": 0,
              "specs": [
                {
                  "title": "loads the profile",
                  "ok": true,
                  "tags": [],
                  "id": "1",
                  "file": "e2e/private/profile.spec.ts",
                  "line": 10,
                  "column": 3,
                  "tests": [
                    {
                      "timeout": 30000,
                      "expectedStatus": "passed",
                      "projectId": "authenticated",
                      "projectName": "authenticated",
                      "results": [
                        { "status": "passed", "duration": 120, "error": null, "errors": [], "retry": 0 }
                      ]
                    }
                  ]
                },
                {
                  "title": "rejects an empty name",
                  "ok": false,
                  "tags": [],
                  "id": "2",
                  "file": "e2e/private/profile.spec.ts",
                  "line": 20,
                  "column": 3,
                  "tests": [
                    {
                      "timeout": 30000,
                      "expectedStatus": "passed",
                      "projectId": "authenticated",
                      "projectName": "authenticated",
                      "results": [
                        {
                          "status": "failed",
                          "duration": 340,
                          "retry": 0,
                          "error": {
                            "message": "Error: expect(received).toBe(expected)",
                            "stack": "Error: expect(received).toBe(expected)\n    at profile.spec.ts:21:9",
                            "snippet": "  20 |   test(\"rejects an empty name\", async () => {"
                          },
                          "errors": []
                        }
                      ]
                    }
                  ]
                }
              ],
              "suites": []
            }
          ]
        }
      ],
      "errors": [],
      "stats": {
        "startTime": "2026-10-22T02:45:54.530Z",
        "duration": 1234.5,
        "expected": 1,
        "unexpected": 1,
        "flaky": 0,
        "skipped": 0
      }
    }
    """;

    private const string ReportWithSkippedAndFlaky = """
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
                    { "status": "failed", "duration": 100, "retry": 0, "error": null, "errors": [] },
                    { "status": "passed", "duration": 90, "retry": 1, "error": null, "errors": [] }
                  ]
                }
              ]
            },
            {
              "title": "is skipped",
              "ok": true,
              "tags": [],
              "id": "2",
              "file": "e2e/public/landing.spec.ts",
              "line": 9,
              "column": 3,
              "tests": [
                {
                  "timeout": 30000,
                  "expectedStatus": "skipped",
                  "projectId": "public",
                  "projectName": "public",
                  "results": [
                    { "status": "skipped", "duration": 0, "retry": 0, "error": null, "errors": [] }
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
        "duration": 190.0,
        "expected": 1,
        "unexpected": 0,
        "flaky": 1,
        "skipped": 1
      }
    }
    """;

    [TestMethod]
    public void Parse_ReadsTheCountsOfTheRun()
    {
        var summary = PlaywrightReportParser.Parse(ReportWithOneFailure);

        summary.TotalTests.Should().Be(2);
        summary.PassedTests.Should().Be(1);
        summary.FailedTests.Should().Be(1);
        summary.FlakyTests.Should().Be(0);
        summary.SkippedTests.Should().Be(0);
        summary.DurationMs.Should().Be(1234);
        summary.StartedAtUtc.Should().Be(new DateTime(2026, 10, 22, 2, 45, 54, 530, DateTimeKind.Utc));
    }

    [TestMethod]
    public void Parse_GroupsTheSpecsByFileAndProject()
    {
        var summary = PlaywrightReportParser.Parse(ReportWithOneFailure);

        summary.Files.Should().HaveCount(1);
        var file = summary.Files[0];
        file.File.Should().Be("e2e/private/profile.spec.ts");
        file.Project.Should().Be("authenticated");
        file.Failed.Should().Be(1);
        file.Passed.Should().Be(1);
        file.Status.Should().Be("failed");
        file.DurationMs.Should().Be(460);
    }

    [TestMethod]
    public void Parse_NamesTheTestWithItsDescribeBlocks()
    {
        var summary = PlaywrightReportParser.Parse(ReportWithOneFailure);

        summary.Files[0].Specs[0].Title.Should().Be("profile › loads the profile");
        summary.Files[0].Specs[1].Title.Should().Be("profile › rejects an empty name");
    }

    [TestMethod]
    public void Parse_KeepsTheFailureOfTheTestThatDidNotPass()
    {
        var summary = PlaywrightReportParser.Parse(ReportWithOneFailure);

        var failed = summary.Files[0].Specs[1];
        failed.Status.Should().Be("failed");
        failed.ErrorMessage.Should().Be("Error: expect(received).toBe(expected)");
        failed.ErrorStack.Should().Contain("profile.spec.ts:21:9");
        failed.ErrorSnippet.Should().Contain("rejects an empty name");
    }

    [TestMethod]
    public void Parse_LeavesTheFailureEmptyForATestThatPassed()
    {
        var summary = PlaywrightReportParser.Parse(ReportWithOneFailure);

        var passed = summary.Files[0].Specs[0];
        passed.Status.Should().Be("passed");
        passed.ErrorMessage.Should().BeNull();
        passed.ErrorStack.Should().BeNull();
    }

    [TestMethod]
    public void Parse_CountsARetryAsFlaky()
    {
        var summary = PlaywrightReportParser.Parse(ReportWithSkippedAndFlaky);

        summary.FlakyTests.Should().Be(1);
        summary.SkippedTests.Should().Be(1);
        summary.Files[0].Specs[0].Status.Should().Be("flaky");
        summary.Files[0].Specs[0].Retries.Should().Be(1);
        summary.Files[0].Specs[1].Status.Should().Be("skipped");
        summary.Files[0].Status.Should().Be("flaky");
    }

    [TestMethod]
    public void Parse_StripsTheColourCodesOfTheFailureMessage()
    {
        var coloured = "\\u001b[31mError\\u001b[39m: expect(received).toBe(expected)";
        var report = ReportWithOneFailure.Replace(
            "Error: expect(received).toBe(expected)",
            coloured,
            StringComparison.Ordinal);

        var summary = PlaywrightReportParser.Parse(report);

        summary.Files[0].Specs[1].ErrorMessage.Should().Be("Error: expect(received).toBe(expected)");
    }

    [TestMethod]
    public void Parse_ReadsASuiteThatHasNoSuiteOfItsOwn()
    {
        // Playwright leaves the "suites" key out of a suite that holds no describe block.
        const string report = """
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
              ]
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

        var summary = PlaywrightReportParser.Parse(report);

        summary.TotalTests.Should().Be(1);
        summary.Files.Should().HaveCount(1);
        summary.Files[0].Specs[0].Title.Should().Be("shows the hero");
    }

    [TestMethod]
    public void Parse_KnowsATestThatPinsAGap()
    {
        var report = ReportWithOneFailure.Replace(
            "loads the profile",
            "BUG B7: the profile never loads",
            StringComparison.Ordinal);

        var summary = PlaywrightReportParser.Parse(report);

        summary.Files[0].Specs[0].IsKnownIssue.Should().BeTrue();
        summary.Files[0].Specs[1].IsKnownIssue.Should().BeFalse();
    }

    [TestMethod]
    public void Parse_CountsTheFailedTestsThatPinAGap()
    {
        var report = ReportWithOneFailure.Replace(
            "rejects an empty name",
            "BUG B7: the empty name is not rejected",
            StringComparison.Ordinal);

        var summary = PlaywrightReportParser.Parse(report);

        summary.KnownIssueTests.Should().Be(1);
    }

    [TestMethod]
    public void Parse_RejectsAnEmptyReport()
    {
        var act = () => PlaywrightReportParser.Parse("  ");

        act.Should().Throw<ArgumentException>();
    }
}
