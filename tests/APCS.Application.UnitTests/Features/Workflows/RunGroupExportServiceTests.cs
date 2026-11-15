using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class RunGroupExportServiceTests
{
    private readonly List<WorkflowRunGroup> _groups = [];
    private readonly List<WorkflowRun> _runs = [];
    private readonly List<ExportPackage> _exports = [];
    private readonly Dictionary<string, byte[]> _files = [];
    private readonly WorkflowRunGroup _group = new() { Id = Guid.NewGuid(), UserId = WorkflowTestData.Owner };

    public RunGroupExportServiceTests() => _groups.Add(_group);

    private RunGroupExportService Service()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(WorkflowTestData.Owner);
        var store = new Mock<ILocalExportStore>();
        store.Setup(x => x.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => _files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);
        var time = new Mock<TimeProvider>();
        time.Setup(x => x.GetUtcNow()).Returns(WorkflowTestData.Now);
        return new(user.Object, WorkflowTestData.Repository(_groups, x => x.Id).Object, WorkflowTestData.Repository(_runs, x => x.Id).Object,
            WorkflowTestData.Repository(_exports, x => x.Id).Object, store.Object, time.Object);
    }

    /// <summary>A video of the group whose run exported its product's folder, as the export worker writes it.</summary>
    private WorkflowRun AddVideo(string folder, string video, string status = "ready", int order = 0)
    {
        var run = new WorkflowRun { Id = Guid.NewGuid(), RunGroupId = _group.Id, UserId = WorkflowTestData.Owner, CreatedAt = WorkflowTestData.Now.UtcDateTime.AddMinutes(order) };
        _runs.Add(run);
        var key = $"local-export/{Guid.NewGuid():D}/{Guid.NewGuid():D}";
        _exports.Add(new ExportPackage
        {
            Id = Guid.NewGuid(), UserId = WorkflowTestData.Owner, WorkflowRunId = run.Id, Status = status, StorageProvider = "local", StorageKey = key,
            Type = "zip_package", Name = "p", Manifest = "{}", CreatedAt = WorkflowTestData.Now.UtcDateTime, ExpiresAt = WorkflowTestData.Now.UtcDateTime.AddDays(7)
        });
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "manifest.json", new JsonObject { ["schemaVersion"] = 2, ["products"] = new JsonArray(new JsonObject { ["folder"] = folder }) }.ToJsonString());
            Write(zip, "README.txt", "Read me");
            Write(zip, $"{folder}/manifest.json", "{}");
            Write(zip, $"{folder}/video/final/video.mp4", video);
        }
        _files[key] = buffer.ToArray();
        return run;
    }

    private static void Write(ZipArchive zip, string path, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), Encoding.UTF8);
        writer.Write(text);
    }

    private static string Read(ZipArchive zip, string path)
    {
        using var reader = new StreamReader(zip.GetEntry(path)!.Open());
        return reader.ReadToEnd();
    }

    [TestMethod]
    public async Task OpenAsync_PutsEveryVideosProductFolderInOneZip()
    {
        AddVideo("Tee-A__a", "first", order: 0);
        AddVideo("Tee-B__b", "second", order: 1);
        // Two designs of one product export the same product folder: the second gets a folder of its own.
        AddVideo("Tee-A__a", "third", order: 2);

        var result = await Service().OpenAsync(_group.Id, default);

        await using var stream = result.Value;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        Read(zip, "Tee-A__a/video/final/video.mp4").Should().Be("first");
        Read(zip, "Tee-B__b/video/final/video.mp4").Should().Be("second");
        Read(zip, "Tee-A__a__2/video/final/video.mp4").Should().Be("third");
        var folders = JsonNode.Parse(Read(zip, "manifest.json"))!["products"]!.AsArray().Select(x => x!["folder"]!.GetValue<string>());
        folders.Should().Equal("Tee-A__a", "Tee-B__b", "Tee-A__a__2");
        Read(zip, "README.txt").Should().Be("Read me");
        zip.Entries.Count(x => x.FullName == "manifest.json").Should().Be(1);
    }

    [TestMethod]
    public async Task SummaryAsync_CountsOnlyTheReadyPackages()
    {
        AddVideo("Tee-A__a", "first");
        AddVideo("Tee-B__b", "second", status: "preparing");

        var summary = (await Service().SummaryAsync(_group.Id, default)).Value;

        summary.Should().BeEquivalentTo(new { Ready = 1, Videos = 2 });
    }

    [TestMethod]
    public async Task OpenAsync_NothingReady_IsNotFound()
    {
        AddVideo("Tee-A__a", "first", status: "preparing");

        (await Service().OpenAsync(_group.Id, default)).Error.Code.Should().Be("ExportNotReady");
    }

    [TestMethod]
    public async Task OpenAsync_AnotherSellersRun_IsNotFound()
    {
        _group.UserId = Guid.NewGuid();

        (await Service().OpenAsync(_group.Id, default)).Error.Code.Should().Be("RunGroupNotFound");
    }
}
