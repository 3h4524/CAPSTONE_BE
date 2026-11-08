namespace APCS.Infrastructure.Services;

/// <summary>
/// The seller a background worker acts for within one DI scope. Set it before calling a use case that checks
/// <see cref="Application.Abstractions.Authentication.ICurrentUser"/>; HTTP requests never set it.
/// </summary>
public sealed class ActingUserContext
{
    public Guid? UserId { get; private set; }

    public void ActAs(Guid userId)
    {
        if (UserId is { } current && current != userId)
            throw new InvalidOperationException("A scope acts for one user only.");
        UserId = userId;
    }
}
