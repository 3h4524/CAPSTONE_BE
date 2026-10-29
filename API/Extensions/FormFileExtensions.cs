using APCS.Application.Abstractions.Storage;
using APCS.Common.Helpers;

namespace APCS.Api.Extensions;

/// <summary>
/// Maps multipart uploads to the Application upload contract.
/// </summary>
public static class FormFileExtensions
{
    /// <summary>
    /// Opens a single optional upload, or returns null when no file was sent.
    /// </summary>
    public static UploadFileDto? ToUploadFileDto(this IFormFile? file) =>
        file is null ? null : new UploadFileDto(
            FileNameHelper.SanitizeFileName(file.FileName),
            file.ContentType,
            file.Length,
            file.OpenReadStream());

    /// <summary>
    /// Opens every file in a multipart attachment collection.
    /// </summary>
    public static IReadOnlyList<UploadFileDto> ToUploadFileDtos(this IEnumerable<IFormFile> files) =>
        files.Select(file => new UploadFileDto(
            FileNameHelper.SanitizeFileName(file.FileName),
            file.ContentType,
            file.Length,
            file.OpenReadStream())).ToArray();

    /// <summary>
    /// Disposes the streams opened for a multipart attachment collection.
    /// </summary>
    public static void DisposeUploads(this IEnumerable<UploadFileDto> uploads)
    {
        foreach (var upload in uploads)
        {
            upload.Content.Dispose();
        }
    }
}
