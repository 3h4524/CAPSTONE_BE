using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows.Validators;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public interface IWorkflowService
{
    Task<Result<IReadOnlyList<WorkflowResponse>>> ListAsync(CancellationToken ct);
    Task<Result<WorkflowResponse>> GetAsync(Guid id, CancellationToken ct);
    Task<Result<WorkflowResponse>> SaveAsync(Guid? id, SaveWorkflowRequest request, CancellationToken ct);
    Task<Result> DeleteAsync(Guid id, long revision, CancellationToken ct);
}

public sealed class WorkflowService(ICurrentUser user, IRepository<Workflow> workflows, IWorkflowStateRepository state,
    IUnitOfWork uow, SaveWorkflowValidator validator, TimeProvider time) : IWorkflowService
{
    public async Task<Result<IReadOnlyList<WorkflowResponse>>> ListAsync(CancellationToken ct) =>
        Result.Success<IReadOnlyList<WorkflowResponse>>((await workflows.FindAsync(x => x.UserId == user.UserId && x.DeletedAt == null, ct)).OrderByDescending(x => x.UpdatedAt).Select(Map).ToArray());
    public async Task<Result<WorkflowResponse>> GetAsync(Guid id, CancellationToken ct)
    {
        var entity = await workflows.GetByIdAsync(id, ct);
        return entity is null || entity.UserId != user.UserId || entity.DeletedAt != null ? Result.Failure<WorkflowResponse>(Missing()) : Result.Success(Map(entity));
    }
    public async Task<Result<WorkflowResponse>> SaveAsync(Guid? id, SaveWorkflowRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Result.Failure<WorkflowResponse>(Error.Validation(string.Join(" ", validation.Errors.Select(x => x.ErrorMessage))));
        await using var tx = await uow.BeginTransactionAsync(ct);
        await state.LockOwnerAsync(user.UserId!.Value, ct);
        Workflow entity;
        if (id.HasValue)
        {
            var found = await state.LockWorkflowAsync(id.Value, ct);
            if (found is null || found.UserId != user.UserId || found.DeletedAt != null) return Result.Failure<WorkflowResponse>(Missing());
            if (request.ExpectedRevision != found.Revision) return Result.Failure<WorkflowResponse>(Error.Conflict("StaleRevision", "Workflow changed. Reload before saving."));
            entity = found;
            entity.Revision++;
        }
        else
        {
            entity = new() { Id = Guid.NewGuid(), UserId = user.UserId.Value, Revision = 1, CreatedAt = time.GetUtcNow().UtcDateTime, SchemaVersion = 2 };
            await workflows.AddAsync(entity, cancellationToken: ct);
        }
        var name = request.Name.Trim();
        if ((await workflows.FindAsync(x => x.UserId == entity.UserId && x.DeletedAt == null && x.Id != entity.Id, ct)).Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure<WorkflowResponse>(Error.Conflict("DuplicateWorkflowName", "Workflow name already exists."));
        entity.Name = name; entity.Description = request.Description.Trim(); entity.Definition = WorkflowJson.Write(request.Definition); entity.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Result.Success(Map(entity));
    }
    public async Task<Result> DeleteAsync(Guid id, long revision, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var entity = await state.LockWorkflowAsync(id, ct);
        if (entity is null || entity.UserId != user.UserId || entity.DeletedAt != null) return Result.Failure(Missing());
        if (entity.Revision != revision) return Result.Failure(Error.Conflict("StaleRevision", "Reload before deleting."));
        entity.DeletedAt = time.GetUtcNow().UtcDateTime; entity.Revision++;
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Result.Success();
    }
    private static Error Missing() => Error.NotFound("WorkflowNotFound", "Workflow not found.");
    private static WorkflowResponse Map(Workflow x) => new(x.Id, x.Name, x.Description, WorkflowJson.Read<WorkflowDefinition>(x.Definition).Nodes.Count, x.Revision, x.CreatedAt, x.UpdatedAt, WorkflowJson.Read<WorkflowDefinition>(x.Definition));
}
