namespace APCS.Api.Extensions;

/// <summary>
/// Loads local dotenv files into process environment variables before configuration is built.
/// </summary>
public static class DotEnvLoader
{
    /// <summary>
    /// Loads the first .env file found from the current directory or its parents.
    /// </summary>
    /// <param name="fileName">The dotenv file name.</param>
    public static void Load(string fileName = ".env")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var filePath = FindFile(fileName);
        if (filePath is null)
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line["export ".Length..].Trim();
            }

            var separatorIndex = line.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = NormalizeValue(line[(separatorIndex + 1)..].Trim());

            if (string.IsNullOrWhiteSpace(key) || Environment.GetEnvironmentVariable(key) is not null)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string? FindFile(string fileName)
    {
        var searchRoots = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var root in searchRoots)
        {
            var directory = new DirectoryInfo(root);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static string NormalizeValue(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
