using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>The packages of a batch video run that can be downloaded together.</summary>
/// <param name="Ready">Videos whose package is ready.</param>
/// <param name="Videos">Videos the run makes.</param>
public sealed record RunGroupExportSummary(Guid GroupId, int Ready, int Videos);

public interface IRunGroupExportService
{
    Task<Result<RunGroupExportSummary>> SummaryAsync(Guid groupId, CancellationToken ct);

    /// <summary>One ZIP with the package of every video of the run that is ready: a folder per product and design.</summary>
    Task<Result<Stream>> OpenAsync(Guid groupId, CancellationToken ct);
}

/// <summary>
/// Each video of a batch video run is exported by its own run (Export ZIP), as one package holding its product's
/// folder. The seller downloads them as one ZIP: every package's product folder side by side, under one manifest.
/// </summary>
public sealed class RunGroupExportService(ICurrentUser user, IRepository<WorkflowRunGroup> groups, IRepository<WorkflowRun> runs,
    IRepository<ExportPackage> exports, ILocalExportStore localExports, TimeProvider time) : IRunGroupExportService
{
    private const string Manifest = "manifest.json";
    private const string Readme = "README.txt";

    public async Task<Result<RunGroupExportSummary>> SummaryAsync(Guid groupId, CancellationToken ct)
    {
        var found = await ReadyAsync(groupId, ct);
        return found.IsFailure
            ? Result.Failure<RunGroupExportSummary>(found.Error)
            : Result.Success(new RunGroupExportSummary(groupId, found.Value.Packages.Count, found.Value.Videos));
    }

    public async Task<Result<Stream>> OpenAsync(Guid groupId, CancellationToken ct)
    {
        var found = await ReadyAsync(groupId, ct);
        if (found.IsFailure) return Result.Failure<Stream>(found.Error);
        if (found.Value.Packages.Count == 0) return Result.Failure<Stream>(NothingReady());

        var output = new FileStream(Path.Combine(Path.GetTempPath(), $"apcs-group-export-{Guid.NewGuid():N}.zip"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            var opened = new List<Stream>();
            try
            {
                foreach (var package in found.Value.Packages)
                {
                    var source = await localExports.OpenReadAsync(package.StorageKey!, ct);
                    if (source is null) return Fail(output, NothingReady());
                    opened.Add(source);
                }
                Merge(opened, output);
            }
            finally
            {
                foreach (var source in opened) await source.DisposeAsync();
            }
            output.Position = 0;
            return Result.Success<Stream>(output);
        }
        catch (InvalidDataException)
        {
            return Fail(output, Error.Failure("ExportUnreadable", "A video package of this run could not be read. Export it again from its video."));
        }
    }

    /// <summary>
    /// Copies every package's folders into one archive. A product with several designs has a package per design, each
    /// with the same product folder: the later ones get a numbered folder of their own.
    /// </summary>
    public static void Merge(IReadOnlyList<Stream> packages, Stream output)
    {
        var products = new JsonArray();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? readme = null;
        using (var merged = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var package in packages)
            {
                using var source = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
                var folders = new Dictionary<string, string>(StringComparer.Ordinal);
                string Folder(string original)
                {
                    if (folders.TryGetValue(original, out var known)) return known;
                    var name = original;
                    for (var copy = 2; !used.Add(name); copy++) name = $"{original}__{copy}";
                    folders[original] = name;
                    return name;
                }

                foreach (var product in ReadProducts(source))
                {
                    if (product["folder"]?.GetValue<string>() is { Length: > 0 } folder) product["folder"] = Folder(folder);
                    products.Add(product);
                }
                foreach (var entry in source.Entries)
                {
                    if (entry.FullName == Readme) { readme ??= ReadText(entry); continue; }
                    if (entry.FullName == Manifest || entry.FullName.EndsWith('/')) continue;
                    var slash = entry.FullName.IndexOf('/');
                    var path = slash < 0 ? Folder("files") + "/" + entry.FullName : Folder(entry.FullName[..slash]) + entry.FullName[slash..];
                    // Videos and images are compressed already: storing them keeps a large download quick to make.
                    var copy = merged.CreateEntry(path, CompressionLevel.NoCompression);
                    copy.LastWriteTime = entry.LastWriteTime;
                    using var from = entry.Open();
                    using var to = copy.Open();
                    from.CopyTo(to);
                }
            }

            WriteText(merged, Manifest, new JsonObject
            {
                ["schemaVersion"] = 2, ["part"] = 1, ["parts"] = 1, ["products"] = products
            }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            WriteText(merged, Readme, readme
                ?? "Each product contains design, mockup, video and listing folders. See each manifest for included and missing files.");
        }
    }

    private static IEnumerable<JsonObject> ReadProducts(ZipArchive source)
    {
        if (source.GetEntry(Manifest) is not { } entry) return [];
        try
        {
            return JsonNode.Parse(ReadText(entry))?["products"] is JsonArray listed
                ? listed.OfType<JsonObject>().Select(product => (JsonObject)product.DeepClone()).ToArray()
                : [];
        }
        catch (JsonException) { return []; }
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void WriteText(ZipArchive archive, string path, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    /// <summary>The group's latest ready package of each of its videos.</summary>
    private async Task<Result<(IReadOnlyList<ExportPackage> Packages, int Videos)>> ReadyAsync(Guid groupId, CancellationToken ct)
    {
        var group = await groups.GetByIdAsync(groupId, ct);
        if (group is null || group.UserId != user.UserId)
            return Result.Failure<(IReadOnlyList<ExportPackage>, int)>(Error.NotFound("RunGroupNotFound", "Run not found."));
        var children = (await runs.FindAsync(x => x.RunGroupId == groupId, ct)).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
        var ids = children.Select(x => (Guid?)x.Id).ToArray();
        if (ids.Length == 0) return Result.Success<(IReadOnlyList<ExportPackage>, int)>(([], 0));
        var now = time.GetUtcNow().UtcDateTime;
        var ready = (await exports.FindAsync(x => ids.Contains(x.WorkflowRunId) && x.UserId == group.UserId && x.Status == "ready"
                && x.StorageProvider == "local" && x.StorageKey != null && x.ExpiresAt > now, ct))
            .GroupBy(x => x.WorkflowRunId!.Value)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(package => package.CreatedAt).First());
        var packages = children.Select(run => ready.GetValueOrDefault(run.Id)).OfType<ExportPackage>().ToArray();
        return Result.Success<(IReadOnlyList<ExportPackage>, int)>((packages, children.Length));
    }

    private static Result<Stream> Fail(Stream output, Error error)
    {
        output.Dispose();
        return Result.Failure<Stream>(error);
    }

    private static Error NothingReady() => Error.NotFound("ExportNotReady", "No video package of this run is ready yet.");
}
