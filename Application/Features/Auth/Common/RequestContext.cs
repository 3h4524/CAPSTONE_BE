namespace APCS.Application.Features.Auth.Common;

/// <summary>
/// Carries the caller's network details into the auth use cases for audit purposes.
/// </summary>
/// <param name="IpAddress">The caller's IP address, when the transport exposed one.</param>
/// <param name="UserAgent">The caller's user agent, when the transport exposed one.</param>
/// <remarks>
/// Set by the API layer from the current request, never bound from the request body: a client
/// must not be able to choose the address recorded against its own session.
/// </remarks>
public sealed record RequestContext(string? IpAddress, string? UserAgent)
{
    /// <summary>
    /// Gets an empty context, for callers with no transport details (background jobs, tests).
    /// </summary>
    public static RequestContext None { get; } = new(null, null);
}
