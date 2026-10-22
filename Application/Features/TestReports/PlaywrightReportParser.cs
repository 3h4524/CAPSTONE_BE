using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using APCS.Application.Features.TestReports.Dtos;

namespace APCS.Application.Features.TestReports;

/// <summary>
/// What a parsed Playwright report says about the run it describes.
/// </summary>
/// <param name="Files">The spec files the run covered, grouped per file and project.</param>
/// <param name="TotalTests">How many tests the run covered.</param>
/// <param name="PassedTests">How many tests passed.</param>
/// <param name="FailedTests">How many tests failed.</param>
/// <param name="FlakyTests">How many tests passed only after a retry.</param>
/// <param name="SkippedTests">How many tests were skipped.</param>
/// <param name="StartedAtUtc">When the run started.</param>
/// <param name="DurationMs">How long the run took, in milliseconds.</param>
public sealed record TestReportSummary(
    IReadOnlyList<TestReportFileDto> Files,
    int TotalTests,
    int PassedTests,
    int FailedTests,
    int FlakyTests,
    int SkippedTests,
    DateTime? StartedAtUtc,
    long DurationMs);

/// <summary>
/// Reads the JSON report Playwright writes and turns it into the shape the admin pages show.
/// </summary>
public static class PlaywrightReportParser
{
    private static readonly Regex AnsiEscape = new("\u001b\\[[0-9;]*m", RegexOptions.Compiled);

    /// <summary>
    /// Parses a Playwright JSON report.
    /// </summary>
    /// <param name="rawReport">The report file, as Playwright wrote it.</param>
    /// <returns>What the run covered and how it ended.</returns>
    public static TestReportSummary Parse(string rawReport)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawReport);

        using var document = JsonDocument.Parse(rawReport);
        var root = document.RootElement;

        var groups = new Dictionary<(string File, string Project), List<TestReportSpecDto>>();
        var order = new List<(string File, string Project)>();

        foreach (var suite in root.GetProperty("suites").EnumerateArray())
        {
            CollectSuite(suite, suite.GetProperty("file").GetString() ?? "unknown", [], groups, order, addOwnTitle: false);
        }

        var files = order
            .Select(key => BuildFile(key.File, key.Project, groups[key]))
            .ToList();

        var stats = root.GetProperty("stats");
        return new TestReportSummary(
            files,
            stats.GetProperty("expected").GetInt32() + stats.GetProperty("unexpected").GetInt32()
                + stats.GetProperty("skipped").GetInt32() + stats.GetProperty("flaky").GetInt32(),
            stats.GetProperty("expected").GetInt32(),
            stats.GetProperty("unexpected").GetInt32(),
            stats.GetProperty("flaky").GetInt32(),
            stats.GetProperty("skipped").GetInt32(),
            ReadStartTime(stats),
            (long)stats.GetProperty("duration").GetDouble());
    }

    private static void CollectSuite(
        JsonElement suite,
        string file,
        IReadOnlyList<string> describePath,
        Dictionary<(string File, string Project), List<TestReportSpecDto>> groups,
        List<(string File, string Project)> order,
        bool addOwnTitle)
    {
        // The suite at the top of the tree is the spec file itself; the ones below it are the
        // describe blocks, and their titles are what names a test.
        var title = ReadString(suite, "title");
        var currentPath = addOwnTitle && !string.IsNullOrWhiteSpace(title)
            ? [.. describePath, title]
            : describePath;

        foreach (var spec in suite.GetProperty("specs").EnumerateArray())
        {
            var test = spec.GetProperty("tests").EnumerateArray().FirstOrDefault();
            if (test.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }

            var project = ReadString(test, "projectName") ?? "default";
            var key = (file, project);
            if (!groups.TryGetValue(key, out var specs))
            {
                specs = [];
                groups[key] = specs;
                order.Add(key);
            }

            specs.Add(ReadSpec(spec, test, currentPath));
        }

        foreach (var child in suite.GetProperty("suites").EnumerateArray())
        {
            CollectSuite(child, file, currentPath, groups, order, addOwnTitle: true);
        }
    }

    private static TestReportSpecDto ReadSpec(JsonElement spec, JsonElement test, IReadOnlyList<string> describePath)
    {
        var results = test.GetProperty("results").EnumerateArray().ToList();
        var lastResult = results.Count > 0 ? results[^1] : default;
        var status = ReadStatus(test, lastResult, results);

        var error = lastResult.ValueKind == JsonValueKind.Object && lastResult.TryGetProperty("error", out var errorElement)
            ? errorElement
            : default;

        return new TestReportSpecDto
        {
            Title = ReadTitle(spec, describePath),
            Status = status,
            DurationMs = ReadDuration(results),
            Retries = Math.Max(0, results.Count - 1),
            ErrorMessage = ReadErrorMessage(error, status),
            ErrorStack = ReadString(error, "stack"),
            ErrorSnippet = ReadString(error, "snippet"),
        };
    }

    private static string ReadTitle(JsonElement spec, IReadOnlyList<string> describePath)
    {
        var title = ReadString(spec, "title") ?? "unnamed test";
        return describePath.Count > 0 ? $"{string.Join(" › ", describePath)} › {title}" : title;
    }

    private static string ReadStatus(JsonElement test, JsonElement lastResult, IReadOnlyList<JsonElement> results)
    {
        var status = ReadString(test, "status");
        if (status == "flaky")
        {
            return "flaky";
        }

        // A test that failed and then passed on a retry is flaky, whatever the run calls it.
        if (results.Count > 1 && ReadString(results[0], "status") is "failed" or "timedOut"
            && ReadString(lastResult, "status") == "passed")
        {
            return "flaky";
        }

        return status switch
        {
            "expected" => "passed",
            "unexpected" => "failed",
            "skipped" => "skipped",
            _ => ReadString(lastResult, "status") ?? "unknown",
        };
    }

    private static long ReadDuration(IReadOnlyList<JsonElement> results)
    {
        long total = 0;
        foreach (var result in results)
        {
            if (result.TryGetProperty("duration", out var duration) && duration.ValueKind == JsonValueKind.Number)
            {
                total += (long)duration.GetDouble();
            }
        }

        return total;
    }

    private static string? ReadErrorMessage(JsonElement error, string status)
    {
        if (status == "failed")
        {
            var message = ReadString(error, "message");
            return message ?? "The test failed without a message.";
        }

        return null;
    }

    private static DateTime? ReadStartTime(JsonElement stats)
    {
        var startTime = ReadString(stats, "startTime");
        return DateTime.TryParse(startTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : AnsiEscape.Replace(text, string.Empty);
    }

    private static TestReportFileDto BuildFile(string file, string project, IReadOnlyList<TestReportSpecDto> specs)
    {
        var failed = specs.Count(spec => spec.Status == "failed");
        var flaky = specs.Count(spec => spec.Status == "flaky");
        var skipped = specs.Count(spec => spec.Status == "skipped");
        var passed = specs.Count(spec => spec.Status == "passed");

        var status = failed > 0 ? "failed" : flaky > 0 ? "flaky" : skipped == specs.Count && specs.Count > 0 ? "skipped" : "passed";

        return new TestReportFileDto
        {
            File = file,
            Project = project,
            DurationMs = specs.Sum(spec => spec.DurationMs),
            Status = status,
            Passed = passed,
            Failed = failed,
            Flaky = flaky,
            Skipped = skipped,
            Specs = specs,
        };
    }
}
