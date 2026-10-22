using System;
using System.IO;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Finds the folders the test reports live in, relative to the repository the API runs from.
/// </summary>
public static class TestReportPaths
{
    /// <summary>
    /// Gets the root of the repository: the folder that holds the solution file.
    /// </summary>
    public static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Capstone.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? Directory.GetCurrentDirectory();
    }
}
