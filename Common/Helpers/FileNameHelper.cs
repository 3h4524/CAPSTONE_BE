namespace APCS.Common.Helpers;

/// <summary>
/// Normalizes upload file names shared by API transport mapping and Application storage keys.
/// </summary>
public static class FileNameHelper
{
    /// <summary>
    /// Strips directory components from a client-supplied file name.
    /// </summary>
    /// <param name="fileName">The raw file name from the upload.</param>
    /// <returns>The file name without directory components.</returns>
    public static string SanitizeFileName(string fileName) =>
        Path.GetFileName(fileName.Replace('\\', '/'));
}
