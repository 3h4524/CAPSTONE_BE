using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.TestReports;

/// <summary>
/// Keeps the seller account the end-to-end suite signs in as.
/// </summary>
/// <remarks>
/// The suite needs a seller with a session, and the person who starts a run has no seller account
/// to hand over. This prepares one: it is created, confirmed and given a fresh random password
/// before the run, and the password is only known while the run lasts. The account and everything
/// the suite left on it are removed afterwards, so the next run starts from the same clean state.
/// </remarks>
public sealed class TestSellerProvisioner(
    IAccountService accounts,
    IRepository<Workflow> workflows,
    IRepository<WorkflowRun> workflowRuns,
    IRepository<WorkflowNodeRun> workflowNodeRuns,
    IRepository<MediaJob> mediaJobs,
    IRepository<PromoVideo> promoVideos,
    IRepository<ExportPackage> exportPackages,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ITestSellerProvisioner
{
    /// <summary>
    /// The address of the account the suite signs in as.
    /// </summary>
    public const string Email = "e2e.seller@apcs.test";

    /// <summary>
    /// The name the account carries.
    /// </summary>
    public const string FullName = "E2E Seller";

    /// <inheritdoc />
    public async Task<TestSellerCredentials> EnsureAsync(CancellationToken cancellationToken = default)
    {
        var password = GeneratePassword();
        var existing = await accounts.FindByEmailAsync(Email, cancellationToken);

        if (existing is not null)
        {
            // A leftover account from an earlier run: a new password and a confirmed address are
            // all it needs.
            await accounts.UpdatePasswordAsync(existing.Id, password, cancellationToken);
            await accounts.ConfirmEmailAsync(existing.Id, timeProvider.GetUtcNow(), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new TestSellerCredentials(Email, password);
        }

        var creation = await accounts.CreateUserAsync(Email, password, FullName, cancellationToken);
        if (!creation.Succeeded || creation.User is null)
        {
            throw new InvalidOperationException(
                $"The seller account for the test suite could not be created: {string.Join(", ", creation.Errors)}");
        }

        // The account is confirmed here instead of through the emailed token: the suite has no
        // inbox to read the link from.
        await accounts.ConfirmEmailAsync(creation.User.Id, timeProvider.GetUtcNow(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TestSellerCredentials(Email, password);
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(CancellationToken cancellationToken = default)
    {
        var existing = await accounts.FindByEmailAsync(Email, cancellationToken);
        if (existing is null)
        {
            return Result.Success();
        }

        try
        {
            await RemoveWorkflowsAsync(existing.Id, cancellationToken);
            await accounts.DeleteUserAsync(existing.Id, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception exception)
        {
            // A row that does not cascade (an invoice, a ticket reply) keeps the account alive.
            return Result.Failure(new Error(
                "test_reports.seller_not_removed",
                $"The seller account of the test suite could not be removed: {exception.Message}",
                ErrorType.Failure));
        }
    }

    /// <summary>
    /// Removes the workflows of the account and everything that hangs off them.
    /// </summary>
    /// <remarks>
    /// The schema does not cascade these rows, so they have to go before the account does.
    /// </remarks>
    private async Task RemoveWorkflowsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var owned = await workflows.FindAsync(workflow => workflow.UserId == userId, cancellationToken);
        foreach (var workflow in owned)
        {
            await RemoveWorkflowRunsAsync(workflow.Id, cancellationToken);
            await workflows.RemoveAsync(workflow, saveChange: false, cancellationToken);
        }
    }

    private async Task RemoveWorkflowRunsAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var remaining = await workflowRuns.FindAsync(run => run.WorkflowId == workflowId, cancellationToken);

        while (remaining.Count > 0)
        {
            // A run holds the reference to its parent, so the run no other run points at goes first.
            var leaves = remaining.Where(run => !remaining.Any(other => other.ParentRunId == run.Id)).ToList();
            if (leaves.Count == 0)
            {
                // A cycle would leave nothing to remove; the account stays and the run says so.
                break;
            }

            foreach (var run in leaves)
            {
                var jobs = await mediaJobs.FindAsync(job => job.WorkflowRunId == run.Id, cancellationToken);
                foreach (var job in jobs)
                {
                    await mediaJobs.RemoveAsync(job, saveChange: false, cancellationToken);
                }

                var nodeRuns = await workflowNodeRuns.FindAsync(
                    nodeRun => nodeRun.WorkflowRunId == run.Id, cancellationToken);
                foreach (var nodeRun in nodeRuns)
                {
                    await workflowNodeRuns.RemoveAsync(nodeRun, saveChange: false, cancellationToken);
                }

                var videos = await promoVideos.FindAsync(video => video.WorkflowRunId == run.Id, cancellationToken);
                foreach (var video in videos)
                {
                    await promoVideos.RemoveAsync(video, saveChange: false, cancellationToken);
                }

                var packages = await exportPackages.FindAsync(
                    package => package.WorkflowRunId == run.Id, cancellationToken);
                foreach (var package in packages)
                {
                    await exportPackages.RemoveAsync(package, saveChange: false, cancellationToken);
                }

                await workflowRuns.RemoveAsync(run, saveChange: false, cancellationToken);
            }

            var left = await workflowRuns.FindAsync(run => run.WorkflowId == workflowId, cancellationToken);
            if (left.Count >= remaining.Count)
            {
                break;
            }

            remaining = left;
        }
    }

    private static string GeneratePassword()
    {
        // Long enough and mixed enough for the password rules of the product.
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var characters = new char[20];

        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return $"E2e!{new string(characters)}";
    }
}
