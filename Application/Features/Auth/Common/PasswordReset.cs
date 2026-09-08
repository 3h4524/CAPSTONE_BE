using APCS.Domain.Entities;

namespace APCS.Application.Features.Auth.Common;

/// <summary>
/// Holds the raw password reset token and its persisted entity for a single issuance.
/// </summary>
internal sealed record PasswordReset(string Token, AuthToken Entity);
