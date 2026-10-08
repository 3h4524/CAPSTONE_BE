namespace APCS.IntegrationTests.TestSupport.Fakes;

/// <summary>
/// Fails any outbound HTTP request the host attempts, so a test can never reach a real provider.
/// </summary>
public sealed class OutboundNetworkBlockedException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="OutboundNetworkBlockedException"/> class.</summary>
    /// <param name="clientName">The named <see cref="HttpClient"/> that tried to send a request.</param>
    /// <param name="requestUri">The destination the request targeted.</param>
    public OutboundNetworkBlockedException(string clientName, Uri? requestUri)
        : base($"Integration tests must not make outbound HTTP requests. Named client '{clientName}' tried to reach '{requestUri}'.")
    {
        ClientName = clientName;
        RequestUri = requestUri;
    }

    /// <summary>Gets the named <see cref="HttpClient"/> that attempted the request.</summary>
    public string ClientName { get; }

    /// <summary>Gets the destination the request targeted.</summary>
    public Uri? RequestUri { get; }
}

/// <summary>
/// The handler installed over the named <see cref="HttpClient"/> registrations the host resolves
/// for outbound calls, so any attempt is reported instead of silently leaving the machine.
/// </summary>
public sealed class BlockedOutboundHandler : HttpMessageHandler
{
    private readonly string _clientName;

    /// <summary>Initializes a new instance of the <see cref="BlockedOutboundHandler"/> class.</summary>
    /// <param name="clientName">The named <see cref="HttpClient"/> the handler guards.</param>
    public BlockedOutboundHandler(string clientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        _clientName = clientName;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        throw new OutboundNetworkBlockedException(_clientName, request?.RequestUri);
    }
}
