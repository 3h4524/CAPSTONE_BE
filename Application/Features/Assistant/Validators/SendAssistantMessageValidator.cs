using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Assistant.Validators;

public sealed class SendAssistantMessageValidator : AbstractValidator<SendAssistantMessageRequestDto>
{
    public SendAssistantMessageValidator()
    {
        RuleFor(request => request.Message)
            .NotEmpty()
            .Must(message => !string.IsNullOrWhiteSpace(message)).WithMessage("Type a message.")
            .MaximumLength(AssistantLimits.MaxMessageLength);
        RuleFor(request => request.Context!).SetValidator(new AssistantClientContextValidator()).When(request => request.Context is not null);
    }
}

public sealed class AssistantClientContextValidator : AbstractValidator<AssistantClientContextDto>
{
    public AssistantClientContextValidator()
    {
        RuleFor(context => context.Route)
            .NotEmpty()
            .MaximumLength(AssistantLimits.MaxRouteLength)
            .Must(route => route.StartsWith('/')).WithMessage("The route must start with '/'.");
        RuleFor(context => context.SelectedNodeIds)
            .Must(ids => ids is null || ids.Count <= AssistantLimits.MaxSelectedNodeIds)
            .WithMessage($"At most {AssistantLimits.MaxSelectedNodeIds} nodes can be selected.");
        RuleForEach(context => context.SelectedNodeIds)
            .MaximumLength(AssistantLimits.MaxIdentifierLength);
        RuleFor(context => context.Canvas!).SetValidator(new AssistantCanvasValidator()).When(context => context.Canvas is not null);
    }
}

public sealed class AssistantCanvasValidator : AbstractValidator<AssistantCanvasDto>
{
    public AssistantCanvasValidator()
    {
        RuleFor(canvas => canvas.Nodes)
            .NotNull()
            .Must(nodes => nodes.Count <= AssistantLimits.MaxCanvasNodes)
            .WithMessage($"The canvas can have at most {AssistantLimits.MaxCanvasNodes} nodes.");
        RuleFor(canvas => canvas.Edges)
            .NotNull()
            .Must(edges => edges.Count <= AssistantLimits.MaxCanvasEdges)
            .WithMessage($"The canvas can have at most {AssistantLimits.MaxCanvasEdges} connections.");
        RuleFor(canvas => canvas.WorkflowName).MaximumLength(AssistantLimits.MaxIdentifierLength * 3);
        RuleForEach(canvas => canvas.Nodes).ChildRules(node =>
        {
            node.RuleFor(item => item.Id).NotEmpty().MaximumLength(AssistantLimits.MaxIdentifierLength);
            node.RuleFor(item => item.Type).NotEmpty().MaximumLength(AssistantLimits.MaxIdentifierLength);
            node.RuleFor(item => item.Label).MaximumLength(AssistantLimits.MaxIdentifierLength);
            node.RuleFor(item => item.Config.GetRawText().Length)
                .LessThanOrEqualTo(AssistantLimits.MaxNodeConfigBytes)
                .WithMessage("A node configuration is too large.");
        });
    }
}
