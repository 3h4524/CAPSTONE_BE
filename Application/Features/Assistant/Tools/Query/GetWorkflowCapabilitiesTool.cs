using APCS.Application.Features.Workflows;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed class GetWorkflowCapabilitiesTool(WorkflowCapabilityRegistry registry) : AssistantTool<NoArguments>
{
    public override string Name => "get_workflow_capabilities";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description => "List which workflow step types and video modes are available.";

    protected override string ParametersSchemaJson => """{"type":"object","properties":{}}""";

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, NoArguments arguments, CancellationToken cancellationToken) =>
        Task.FromResult(AssistantToolOutcome.Ok(registry.Get()));
}
