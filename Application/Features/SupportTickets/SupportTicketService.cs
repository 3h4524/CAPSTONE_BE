using System.Security.Cryptography;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Common.Validation;
using APCS.Application.Features.SupportTickets.Common;
using APCS.Application.Features.SupportTickets.Dtos.Request;
using APCS.Application.Features.SupportTickets.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.SupportTickets;

/// <summary>Coordinates Seller and administrator support-ticket workflows.</summary>
public sealed class SupportTicketService(
    ISupportTicketRepository supportTicketRepository,
    IRepository<TicketReply> replyRepository,
    IRepository<TicketAttachment> attachmentRepository,
    IRepository<NotificationAlert> alertRepository,
    IRepository<NotificationDelivery> deliveryRepository,
    IAccountRepository accountRepository,
    IUnitOfWork unitOfWork,
    IFileStorageService fileStorageService,
    IEmailService emailService,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IValidator<CreateSupportTicketRequestDto> createValidator,
    IValidator<ListSupportTicketsRequestDto> listValidator,
    IValidator<CreateTicketReplyRequestDto> replyValidator,
    IValidator<CreateAdminTicketReplyRequestDto> adminReplyValidator,
    IValidator<UpdateSupportTicketRequestDto> updateValidator,
    IValidator<RateSupportTicketRequestDto> ratingValidator,
    ILogger<SupportTicketService> logger) : ISupportTicketService
{
    public async Task<Result<SupportTicketSummaryResponseDto>> CreateAsync(
        CreateSupportTicketRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<SupportTicketSummaryResponseDto>(validation.ToValidationError());
        }

        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Result.Failure<SupportTicketSummaryResponseDto>(SupportTicketErrors.Unauthenticated());
        }

        var now = timeProvider.GetUtcNow();
        var ticketId = Guid.NewGuid();
        var ticketNumber = await GenerateTicketNumberAsync(now, cancellationToken);
        if (ticketNumber is null)
        {
            return Result.Failure<SupportTicketSummaryResponseDto>(SupportTicketErrors.TicketNumberUnavailable());
        }

        var admins = await accountRepository.GetActiveUsersByRoleAsync(AuthConstants.AdminRole, cancellationToken);
        var uploads = await UploadFilesAsync(ticketId, request.Attachments, cancellationToken);
        var ticket = new SupportTicket
        {
            Id = ticketId,
            UserId = userId.Value,
            TicketNumber = ticketNumber,
            Subject = request.Subject.Trim(),
            Description = request.Description.Trim(),
            Category = request.Category,
            Priority = request.Priority,
            Status = SupportTicketStatuses.Open,
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };

        var emailDeliveries = new List<(User Admin, NotificationDelivery Delivery)>();
        IUnitOfWorkTransaction? transaction = null;
        try
        {
            transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            await supportTicketRepository.AddAsync(ticket, cancellationToken: cancellationToken);
            foreach (var upload in uploads)
            {
                await attachmentRepository.AddAsync(
                    CreateAttachment(upload, ticketId, null, userId.Value, now),
                    cancellationToken: cancellationToken);
            }

            foreach (var admin in admins)
            {
                var alert = new NotificationAlert
                {
                    Id = Guid.NewGuid(),
                    UserId = admin.Id,
                    Type = "support_ticket_created",
                    Title = $"New support ticket {ticketNumber}",
                    Message = $"A {request.Priority} priority {request.Category} ticket was created.",
                    Severity = "info",
                    IsRead = false,
                    ActionUrl = $"/admin/support-tickets/{ticketId}",
                    CreatedAt = now.UtcDateTime
                };
                var inAppDelivery = NewDelivery(alert.Id, "in_app", "sent", now, now);
                var emailDelivery = NewDelivery(alert.Id, "email", "pending", now, null);

                await alertRepository.AddAsync(alert, cancellationToken: cancellationToken);
                await deliveryRepository.AddAsync(inAppDelivery, cancellationToken: cancellationToken);
                await deliveryRepository.AddAsync(emailDelivery, cancellationToken: cancellationToken);
                emailDeliveries.Add((admin, emailDelivery));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
            }
            finally
            {
                await CleanupUploadsAsync(uploads);
            }
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        await DeliverAdminEmailsAsync(emailDeliveries, ticket, cancellationToken);
        return Result.Success(MapSummary(ticket));
    }

    public async Task<Result<PagedResult<SupportTicketSummaryResponseDto>>> ListMineAsync(
        ListSupportTicketsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Result.Failure<PagedResult<SupportTicketSummaryResponseDto>>(SupportTicketErrors.Unauthenticated());
        }

        return await ListAsync(userId, request, cancellationToken);
    }

    public async Task<Result<SupportTicketDetailResponseDto>> GetMineAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Result.Failure<SupportTicketDetailResponseDto>(SupportTicketErrors.Unauthenticated());
        }

        var ticket = await supportTicketRepository.GetDetailsAsync(
            id, userId, includeInternalNotes: false, cancellationToken);
        return ticket is null
            ? Result.Failure<SupportTicketDetailResponseDto>(SupportTicketErrors.NotFound())
            : Result.Success(MapDetail(ticket));
    }

    public async Task<Result<SupportTicketReplyResponseDto>> ReplyAsync(
        Guid id,
        CreateTicketReplyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await replyValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<SupportTicketReplyResponseDto>(validation.ToValidationError());
        }

        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Result.Failure<SupportTicketReplyResponseDto>(SupportTicketErrors.Unauthenticated());
        }

        var ticket = await supportTicketRepository.GetForUpdateAsync(id, cancellationToken);
        if (ticket is null || ticket.UserId != userId)
        {
            return Result.Failure<SupportTicketReplyResponseDto>(SupportTicketErrors.NotFound());
        }

        if (ticket.Status is SupportTicketStatuses.Resolved or SupportTicketStatuses.Closed)
        {
            return Result.Failure<SupportTicketReplyResponseDto>(SupportTicketErrors.ReplyNotAllowed());
        }

        var now = timeProvider.GetUtcNow();
        var replyId = Guid.NewGuid();
        var uploads = await UploadFilesAsync(ticket.Id, request.Attachments, cancellationToken);
        var reply = new TicketReply
        {
            Id = replyId,
            SupportTicketId = ticket.Id,
            AuthorId = userId.Value,
            ReplyText = request.ReplyText.Trim(),
            IsInternalNote = false,
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime,
            Author = ticket.User
        };

        IUnitOfWorkTransaction? transaction = null;
        try
        {
            transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            await replyRepository.AddAsync(reply, cancellationToken: cancellationToken);
            foreach (var upload in uploads)
            {
                var attachment = CreateAttachment(upload, null, reply.Id, userId.Value, now);
                attachment.TicketReply = reply;
                reply.TicketAttachments.Add(attachment);
                await attachmentRepository.AddAsync(attachment, cancellationToken: cancellationToken);
            }

            if (ticket.Status == SupportTicketStatuses.WaitingCustomer)
            {
                ticket.Status = SupportTicketStatuses.InProgress;
            }

            ticket.UpdatedAt = now.UtcDateTime;
            await supportTicketRepository.UpdateAsync(ticket, cancellationToken: cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
            }
            finally
            {
                await CleanupUploadsAsync(uploads);
            }
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        return Result.Success(MapReply(reply, ticket.UserId));
    }

    public async Task<Result<SupportTicketSummaryResponseDto>> RateAsync(
        Guid id,
        RateSupportTicketRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ratingValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<SupportTicketSummaryResponseDto>(validation.ToValidationError());
        }

        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Result.Failure<SupportTicketSummaryResponseDto>(SupportTicketErrors.Unauthenticated());
        }

        var ticket = await supportTicketRepository.GetForUpdateAsync(id, cancellationToken);
        if (ticket is null || ticket.UserId != userId)
        {
            return Result.Failure<SupportTicketSummaryResponseDto>(SupportTicketErrors.NotFound());
        }

        if (ticket.Status != SupportTicketStatuses.Resolved || ticket.SatisfactionRating.HasValue)
        {
            return Result.Failure<SupportTicketSummaryResponseDto>(SupportTicketErrors.RatingNotAllowed());
        }

        ticket.SatisfactionRating = request.Rating;
        ticket.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await supportTicketRepository.UpdateAsync(ticket, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(MapSummary(ticket));
    }

    public async Task<Result<PagedResult<SupportTicketSummaryResponseDto>>> ListAdminAsync(
        ListSupportTicketsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (GetAuthenticatedUserId() is null)
        {
            return Result.Failure<PagedResult<SupportTicketSummaryResponseDto>>(SupportTicketErrors.Unauthenticated());
        }

        return await ListAsync(null, request, cancellationToken);
    }

    public async Task<Result<SupportTicketDetailResponseDto>> GetAdminAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (GetAuthenticatedUserId() is null)
        {
            return Result.Failure<SupportTicketDetailResponseDto>(SupportTicketErrors.Unauthenticated());
        }

        var ticket = await supportTicketRepository.GetDetailsAsync(
            id, ownerId: null, includeInternalNotes: true, cancellationToken);
        return ticket is null
            ? Result.Failure<SupportTicketDetailResponseDto>(SupportTicketErrors.NotFound())
            : Result.Success(MapDetail(ticket));
    }

    public async Task<Result<SupportTicketDetailResponseDto>> UpdateAdminAsync(
        Guid id,
        UpdateSupportTicketRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<SupportTicketDetailResponseDto>(validation.ToValidationError());
        }

        if (GetAuthenticatedUserId() is null)
        {
            return Result.Failure<SupportTicketDetailResponseDto>(SupportTicketErrors.Unauthenticated());
        }

        var ticket = await supportTicketRepository.GetForUpdateAsync(id, cancellationToken);
        if (ticket is null)
        {
            return Result.Failure<SupportTicketDetailResponseDto>(SupportTicketErrors.NotFound());
        }

        if (request.AssignedTo.HasValue &&
            !await accountRepository.IsActiveUserInRoleAsync(
                request.AssignedTo.Value,
                AuthConstants.AdminRole,
                cancellationToken))
        {
            return Result.Failure<SupportTicketDetailResponseDto>(SupportTicketErrors.InvalidAssignee());
        }

        if (request.Status is not null && request.Status != ticket.Status &&
            !CanTransition(ticket.Status, request.Status))
        {
            return Result.Failure<SupportTicketDetailResponseDto>(
                SupportTicketErrors.InvalidTransition(ticket.Status, request.Status));
        }

        var now = timeProvider.GetUtcNow();
        ticket.AssignedTo = request.AssignedTo ?? ticket.AssignedTo;
        ticket.Priority = request.Priority ?? ticket.Priority;
        if (request.Status is not null && request.Status != ticket.Status)
        {
            ticket.Status = request.Status;
            if (request.Status == SupportTicketStatuses.Resolved)
            {
                ticket.ResolvedAt = now.UtcDateTime;
            }
        }

        ticket.UpdatedAt = now.UtcDateTime;
        await supportTicketRepository.UpdateAsync(ticket, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = await supportTicketRepository.GetDetailsAsync(
            id, ownerId: null, includeInternalNotes: true, cancellationToken);
        return Result.Success(MapDetail(updated!));
    }

    public async Task<Result<SupportTicketReplyResponseDto>> ReplyAdminAsync(
        Guid id,
        CreateAdminTicketReplyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await adminReplyValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<SupportTicketReplyResponseDto>(validation.ToValidationError());
        }

        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Result.Failure<SupportTicketReplyResponseDto>(SupportTicketErrors.Unauthenticated());
        }

        var ticket = await supportTicketRepository.GetForUpdateAsync(id, cancellationToken);
        if (ticket is null)
        {
            return Result.Failure<SupportTicketReplyResponseDto>(SupportTicketErrors.NotFound());
        }

        var author = await accountRepository.GetByIdAsync(userId.Value, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var replyId = Guid.NewGuid();
        var uploads = await UploadFilesAsync(ticket.Id, request.Attachments, cancellationToken);
        var reply = new TicketReply
        {
            Id = replyId,
            SupportTicketId = ticket.Id,
            AuthorId = userId.Value,
            ReplyText = request.ReplyText.Trim(),
            IsInternalNote = request.IsInternalNote,
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime,
            Author = author!
        };

        IUnitOfWorkTransaction? transaction = null;
        try
        {
            transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            await replyRepository.AddAsync(reply, cancellationToken: cancellationToken);
            foreach (var upload in uploads)
            {
                var attachment = CreateAttachment(upload, null, reply.Id, userId.Value, now);
                attachment.TicketReply = reply;
                reply.TicketAttachments.Add(attachment);
                await attachmentRepository.AddAsync(attachment, cancellationToken: cancellationToken);
            }

            ticket.UpdatedAt = now.UtcDateTime;
            await supportTicketRepository.UpdateAsync(ticket, cancellationToken: cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
            }
            finally
            {
                await CleanupUploadsAsync(uploads);
            }
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        return Result.Success(MapReply(reply, ticket.UserId));
    }

    private async Task<Result<PagedResult<SupportTicketSummaryResponseDto>>> ListAsync(
        Guid? ownerId,
        ListSupportTicketsRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await listValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<PagedResult<SupportTicketSummaryResponseDto>>(validation.ToValidationError());
        }

        var (items, totalCount) = await supportTicketRepository.ListAsync(
            ownerId,
            request.Status,
            request.Category,
            request.Priority,
            request.PageNumber,
            request.PageSize,
            cancellationToken);
        return Result.Success(new PagedResult<SupportTicketSummaryResponseDto>(
            items.Select(MapSummary).ToArray(),
            totalCount,
            request.PageNumber,
            request.PageSize));
    }

    private Guid? GetAuthenticatedUserId() =>
        currentUser.IsAuthenticated ? currentUser.UserId : null;

    private async Task<string?> GenerateTicketNumberAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < SupportTicketNumberRules.MaximumGenerationAttempts; attempt++)
        {
            var suffix = string.Create(SupportTicketNumberRules.SuffixLength, (object?)null, static (span, _) =>
            {
                for (var index = 0; index < span.Length; index++)
                {
                    span[index] = SupportTicketNumberRules.Alphabet[
                        RandomNumberGenerator.GetInt32(SupportTicketNumberRules.Alphabet.Length)];
                }
            });
            var number = $"{SupportTicketNumberRules.Prefix}-{now:yyyyMMdd}-{suffix}";
            if (!await supportTicketRepository.TicketNumberExistsAsync(number, cancellationToken))
            {
                return number;
            }
        }

        return null;
    }

    private async Task<List<UploadedAttachment>> UploadFilesAsync(
        Guid ticketId,
        IReadOnlyCollection<UploadFileDto> files,
        CancellationToken cancellationToken)
    {
        var uploads = new List<UploadedAttachment>(files.Count);
        try
        {
            foreach (var file in files)
            {
                var attachmentId = Guid.NewGuid();
                var extension = Path.GetExtension(SanitizeFileName(file.FileName)).ToLowerInvariant();
                var key = $"support-tickets/{ticketId:N}/{attachmentId:N}{extension}";
                var stored = await fileStorageService.UploadAsync(file, key, cancellationToken);
                uploads.Add(new UploadedAttachment(attachmentId, file, stored));
            }

            return uploads;
        }
        catch
        {
            await CleanupUploadsAsync(uploads);
            throw;
        }
    }

    private async Task CleanupUploadsAsync(IEnumerable<UploadedAttachment> uploads)
    {
        foreach (var upload in uploads)
        {
            try
            {
                await fileStorageService.DeleteAsync(upload.Stored.StorageKey, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not clean up support-ticket attachment {AttachmentId}.", upload.Id);
            }
        }
    }

    private async Task DeliverAdminEmailsAsync(
        IEnumerable<(User Admin, NotificationDelivery Delivery)> deliveries,
        SupportTicket ticket,
        CancellationToken cancellationToken)
    {
        foreach (var (admin, delivery) in deliveries)
        {
            try
            {
                await emailService.SendSupportTicketCreatedAsync(
                    admin.Email,
                    admin.FullName,
                    ticket.Id,
                    ticket.TicketNumber,
                    ticket.Category,
                    ticket.Priority,
                    cancellationToken);
                delivery.DeliveryStatus = "sent";
                delivery.SentAt = timeProvider.GetUtcNow().UtcDateTime;
                delivery.ErrorMessage = null;
            }
            catch (Exception exception)
            {
                delivery.DeliveryStatus = "failed";
                delivery.RetryCount = (delivery.RetryCount ?? 0) + 1;
                delivery.ErrorMessage = "Email delivery failed.";
                logger.LogWarning(
                    exception,
                    "Could not email administrator {AdministratorId} about ticket {TicketId}.",
                    admin.Id,
                    ticket.Id);
            }
        }

        try
        {
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not persist email delivery status for ticket {TicketId}.", ticket.Id);
        }
    }

    private TicketAttachment CreateAttachment(
        UploadedAttachment upload,
        Guid? ticketId,
        Guid? replyId,
        Guid uploadedBy,
        DateTimeOffset now) =>
        new()
        {
            Id = upload.Id,
            SupportTicketId = ticketId,
            TicketReplyId = replyId,
            FileUrl = upload.Stored.StableUrl,
            FileName = SanitizeFileName(upload.File.FileName),
            MimeType = upload.File.ContentType,
            FileSizeMb = Math.Round((decimal)upload.File.Length / (1024 * 1024), 4),
            UploadedBy = uploadedBy,
            CreatedAt = now.UtcDateTime
        };

    private NotificationDelivery NewDelivery(
        Guid alertId,
        string channel,
        string status,
        DateTimeOffset createdAt,
        DateTimeOffset? sentAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            NotificationAlertId = alertId,
            Channel = channel,
            DeliveryStatus = status,
            SentAt = sentAt?.UtcDateTime,
            RetryCount = 0,
            CreatedAt = createdAt.UtcDateTime
        };

    private SupportTicketDetailResponseDto MapDetail(SupportTicket ticket) =>
        new(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Subject,
            ticket.Description,
            ticket.Category,
            ticket.Priority,
            ticket.Status,
            ticket.SatisfactionRating,
            ticket.UserId,
            ticket.User.FullName,
            ticket.AssignedTo,
            ticket.AssignedToNavigation?.FullName,
            AsUtc(ticket.CreatedAt),
            AsUtc(ticket.UpdatedAt),
            ticket.ResolvedAt is null ? null : AsUtc(ticket.ResolvedAt),
            ticket.TicketAttachments.OrderBy(item => item.CreatedAt).Select(MapAttachment).ToArray(),
            ticket.TicketReplies.OrderBy(item => item.CreatedAt).Select(item => MapReply(item, ticket.UserId)).ToArray());

    private SupportTicketReplyResponseDto MapReply(TicketReply reply, Guid requesterId) =>
        new(
            reply.Id,
            reply.AuthorId,
            reply.Author.FullName,
            reply.AuthorId == requesterId ? AuthConstants.UserRole : AuthConstants.AdminRole,
            reply.ReplyText,
            reply.IsInternalNote == true,
            AsUtc(reply.CreatedAt),
            reply.TicketAttachments.OrderBy(item => item.CreatedAt).Select(MapAttachment).ToArray());

    private SupportTicketAttachmentResponseDto MapAttachment(TicketAttachment attachment)
    {
        var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant();
        var key = $"support-tickets/{GetAttachmentTicketId(attachment):N}/{attachment.Id:N}{extension}";
        var download = fileStorageService.CreateSignedDownload(key, attachment.FileName);
        return new SupportTicketAttachmentResponseDto(
            attachment.Id,
            attachment.FileName,
            attachment.MimeType,
            attachment.FileSizeMb,
            download.Url,
            download.ExpiresAtUtc);
    }

    private static Guid GetAttachmentTicketId(TicketAttachment attachment) =>
        attachment.SupportTicketId
        ?? attachment.TicketReply?.SupportTicketId
        ?? throw new InvalidOperationException("The attachment is not linked to a support ticket.");

    private static string SanitizeFileName(string fileName) =>
        Path.GetFileName(fileName.Replace('\\', '/'));

    private static SupportTicketSummaryResponseDto MapSummary(SupportTicket ticket) =>
        new(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Subject,
            ticket.Category,
            ticket.Priority,
            ticket.Status,
            ticket.SatisfactionRating,
            AsUtc(ticket.CreatedAt),
            AsUtc(ticket.UpdatedAt));

    private static DateTimeOffset AsUtc(DateTime? value) =>
        new(DateTime.SpecifyKind(value ?? DateTime.UnixEpoch, DateTimeKind.Utc));

    private static bool CanTransition(string current, string requested) => current switch
    {
        SupportTicketStatuses.Open => requested is SupportTicketStatuses.InProgress or SupportTicketStatuses.Closed,
        SupportTicketStatuses.InProgress => requested is SupportTicketStatuses.WaitingCustomer
            or SupportTicketStatuses.Resolved
            or SupportTicketStatuses.Closed,
        SupportTicketStatuses.WaitingCustomer => requested is SupportTicketStatuses.InProgress
            or SupportTicketStatuses.Resolved
            or SupportTicketStatuses.Closed,
        SupportTicketStatuses.Resolved => requested == SupportTicketStatuses.Closed,
        _ => false
    };

    private sealed record UploadedAttachment(Guid Id, UploadFileDto File, StoredFileDto Stored);
}
