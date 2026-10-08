using System.Linq.Expressions;
using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Workflows;
using APCS.Application.Features.Workflows.Validators;
using APCS.Domain.Entities;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

internal sealed class WorkflowTestData
{
    public static readonly Guid Owner = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    public FakeTimeProvider Time { get; } = new(Now);
    public List<Workflow> WorkflowRows { get; } = [];
    public List<WorkflowRun> RunRows { get; } = [];
    public List<WorkflowNodeRun> NodeRows { get; } = [];
    public List<Product> ProductRows { get; } = [];
    public List<MockupImage> AssetRows { get; } = [];
    public List<PromoVideo> VideoRows { get; } = [];
    public List<PromoVideoScene> SceneRows { get; } = [];
    public List<MediaJob> JobRows { get; } = [];
    public List<VideoTemplate> TemplateRows { get; } = [];
    public List<ExportPackage> ExportRows { get; } = [];
    public List<ExportPackageItem> ItemRows { get; } = [];
    public Mock<IRepository<Workflow>> Workflows { get; }
    public Mock<IRepository<WorkflowRun>> Runs { get; }
    public Mock<IRepository<WorkflowNodeRun>> Nodes { get; }
    public Mock<IRepository<Product>> Products { get; }
    public Mock<IRepository<MockupImage>> Assets { get; }
    public Mock<IRepository<PromoVideo>> Videos { get; }
    public Mock<IRepository<PromoVideoScene>> Scenes { get; }
    public Mock<IRepository<MediaJob>> Jobs { get; }
    public Mock<IRepository<VideoTemplate>> Templates { get; }
    public Mock<IRepository<ExportPackage>> Exports { get; }
    public Mock<IRepository<ExportPackageItem>> Items { get; }
    public Mock<IWorkflowStateRepository> State { get; } = new();
    public Mock<IUnitOfWork> Unit { get; } = new();
    public Mock<IUnitOfWorkTransaction> Transaction { get; } = new();
    public Mock<IMediaStorage> Storage { get; } = new();
    public WorkflowCapabilityRegistry Capabilities { get; } = new();
    public WorkflowRuntime Runtime { get; }
    public WorkflowService WorkflowService { get; }
    public WorkflowRunService RunService { get; }
    public MediaJobService MediaService { get; }
    public MockupAssetService MockupService { get; }
    public VideoArtifactService ArtifactService { get; }

    public WorkflowTestData()
    {
        Workflows = Repository(WorkflowRows, x => x.Id);
        Runs = Repository(RunRows, x => x.Id);
        Nodes = Repository(NodeRows, x => x.Id);
        Products = Repository(ProductRows, x => x.Id);
        Assets = Repository(AssetRows, x => x.Id);
        Videos = Repository(VideoRows, x => x.Id);
        Scenes = Repository(SceneRows, x => x.Id);
        Jobs = Repository(JobRows, x => x.Id);
        Templates = Repository(TemplateRows, x => x.Id);
        Exports = Repository(ExportRows, x => x.Id);
        Items = Repository(ItemRows, x => x.Id);
        State.Setup(x => x.LockWorkflowAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => WorkflowRows.SingleOrDefault(x => x.Id == id));
        State.Setup(x => x.LockRunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => RunRows.SingleOrDefault(x => x.Id == id));
        State.Setup(x => x.LockJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => JobRows.SingleOrDefault(x => x.Id == id));
        State.Setup(x => x.LockMockupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => AssetRows.SingleOrDefault(x => x.Id == id));
        Unit.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Transaction.Object);
        var current = new Mock<ICurrentUser>();
        current.SetupGet(x => x.UserId).Returns(Owner);
        Runtime = new(Nodes.Object, Products.Object, Assets.Object, Videos.Object, Scenes.Object, Templates.Object,
            Jobs.Object, Exports.Object, Items.Object, new(), Time);
        WorkflowService = new(current.Object, Workflows.Object, State.Object, Unit.Object, new(), Time);
        RunService = new(current.Object, Workflows.Object, Runs.Object, Nodes.Object, Products.Object, Videos.Object,
            Jobs.Object, State.Object, Unit.Object, new(Capabilities, new()), Runtime, new(), Capabilities, Time);
        MediaService = new(Jobs.Object, Nodes.Object, Assets.Object, Videos.Object, Scenes.Object, Exports.Object,
            Items.Object, State.Object, Unit.Object, Storage.Object, Runtime, Time);
        MockupService = new(current.Object, Products.Object, Assets.Object, State.Object, Unit.Object, Storage.Object, new(), new(), Time);
        ArtifactService = new(current.Object, Runs.Object, Videos.Object, Scenes.Object, Exports.Object, Storage.Object, Time);
        foreach (var version in new[] { 1, 2 })
        foreach (var code in new[] { "product_showcase", "design_detail", "variant_showcase" })
            TemplateRows.Add(new() { Id = Guid.NewGuid(), Code = code, Name = code, Type = "product_showcase",
                Platform = "etsy", DurationSeconds = 12, AspectRatio = "1:2", Resolution = "1080x2160",
                EffectsConfig = version == 2
                    ? WorkflowJson.Write(new { description = "Template description", defaultTransition = "fade", defaultMotionPreset = "varied",
                        requirements = code switch { "design_detail" => new { minimumAssets = 1, requiresDetail = true, minimumVariants = 0 },
                            "variant_showcase" => new { minimumAssets = 2, requiresDetail = false, minimumVariants = 2 },
                            _ => new { minimumAssets = 1, requiresDetail = false, minimumVariants = 0 } } })
                    : "{}", IsSystemTemplate = true, IsActive = true, TemplateVersion = version });
    }

    public Product AddProduct(string type = "tshirt")
    {
        var product = new Product { Id = Guid.NewGuid(), UserId = Owner, BatchId = Guid.NewGuid(), ProductType = type };
        ProductRows.Add(product);
        return product;
    }

    public Workflow AddWorkflow(Product product, GenerateVideoConfig? config = null)
    {
        var workflow = new Workflow { Id = Guid.NewGuid(), UserId = Owner, Name = "Video workflow", Description = "",
            Revision = 4, SchemaVersion = 2, Definition = WorkflowJson.Write(Definition(product, config)),
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime };
        WorkflowRows.Add(workflow);
        return workflow;
    }

    public WorkflowRun AddRun(Product product, string status = "running")
    {
        var workflow = AddWorkflow(product);
        var run = new WorkflowRun { Id = Guid.NewGuid(), WorkflowId = workflow.Id, ProductId = product.Id, UserId = Owner,
            DefinitionSnapshot = workflow.Definition, WorkflowRevision = 4, Revision = 7, Status = status,
            IdempotencyKey = "existing", RequestHash = "existing", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime };
        RunRows.Add(run);
        foreach (var node in Definition(product).Nodes)
            NodeRows.Add(new() { Id = Guid.NewGuid(), WorkflowRunId = run.Id, NodeId = node.Id, NodeType = node.Type,
                Status = "pending", Attempt = 1, InputSnapshot = node.Config.GetRawText(), OutputSnapshot = "{}",
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        return run;
    }

    public MockupImage AddAsset(Product product, string role = "Hero")
    {
        var asset = Asset(product.Id, role);
        AssetRows.Add(asset);
        return asset;
    }

    public MediaJob AddJob(WorkflowRun run, string kind = "render")
    {
        var node = NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == (kind == "render" ? "generate-video" : "export-zip"));
        node.Status = "running";
        var job = new MediaJob { Id = Guid.NewGuid(), UserId = Owner, WorkflowRunId = run.Id, WorkflowNodeRunId = node.Id,
            Kind = kind, Status = "leased", LeaseToken = Guid.NewGuid(), LeaseExpiresAt = Now.AddMinutes(2).UtcDateTime,
            Attempt = 1, MaximumAttempts = 3, Payload = "{}", Progress = 40 };
        JobRows.Add(job);
        return job;
    }

    public static MockupImage Asset(Guid productId, string role = "Hero") => new() { Id = Guid.NewGuid(), ProductId = productId,
        Role = role, ArtworkGroupKey = "artwork-main", MetadataRevision = 3, ApprovedRevision = 3,
        ApprovalStatus = "approved", ContentHash = new string('a', 64), MockupWidthPx = 1080, MockupHeightPx = 2160,
        Regions = WorkflowJson.Write(new MockupRegions(new(.5, .5), new(.25, .2, .5, .6))),
        StorageKey = "private/mockup", StorageVersion = "1", SourceType = "uploaded" };

    public static WorkflowDefinition Definition(Product product, GenerateVideoConfig? config = null)
    {
        var nodes = WorkflowCapabilityRegistry.Pipeline.Select((type, i) => new WorkflowNode("node-" + i, type, type,
            new(i * 200, 0), JsonSerializer.SerializeToElement<object>(type switch
            {
                "product-input" => new ProductInputConfig(product.BatchId, product.Id),
                "apply-mockup" => new MockupConfig(),
                "generate-video" => config ?? new GenerateVideoConfig(),
                _ => new { }
            }, WorkflowJson.Options))).ToArray();
        return new(2, nodes, nodes.Skip(1).Select((node, i) => new WorkflowEdge("edge-" + i, nodes[i].Id, node.Id)).ToArray());
    }

    public static string OutputKey(MediaJob job, string suffix) => $"workflow-media/{job.UserId}/{job.WorkflowRunId}/{job.Id}/{job.LeaseToken}/{suffix}";
    public static MediaQa Qa() => new("h264", "yuv420p", 1080, 2160, 30, 12, 1024 * 1024, false, true);

    private static Mock<IRepository<T>> Repository<T>(List<T> rows, Func<T, Guid> idSelector) where T : class
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => rows.SingleOrDefault(x => idSelector(x) == (Guid)id));
        mock.Setup(x => x.FindAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) => (IReadOnlyList<T>)rows.Where(predicate.Compile()).ToArray());
        mock.Setup(x => x.AddAsync(It.IsAny<T>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((T entity, bool _, CancellationToken _) => rows.Add(entity)).Returns(Task.CompletedTask);
        return mock;
    }
}
